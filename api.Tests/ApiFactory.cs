using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace api.Tests;

/// <summary>
/// Boots the real Program.cs pipeline in memory against a throwaway database.
///
/// TWO MODES, chosen by the <see cref="TestDatabaseVariable"/> environment variable:
///
///   UNSET — SQLite in-memory, built from the model with EnsureCreated. Fast, needs
///   nothing installed, and is what a developer gets by default.
///
///   SET — a real PostgreSQL server (CI runs a postgres:16 service container). Each
///   factory creates its OWN uniquely named database, applies the EF migrations to it,
///   and drops it afterwards.
///
/// Neither mode is the EF in-memory provider, deliberately: it does not enforce unique
/// indexes, so "duplicate email returns 409" would pass there even with the index deleted.
///
/// The PostgreSQL mode earns its place rather than duplicating the SQLite one:
///
///   * It is the only mode that runs the MIGRATIONS. EnsureCreated builds the schema
///     straight from the model and never executes a migration, so a migration that is
///     broken, missing, or out of step with the model is invisible on SQLite.
///   * It is the only mode where the jsonb columns are really jsonb — AppDbContext falls
///     back to TEXT on SQLite, so column-type behaviour is untested there.
///   * It is the real provider, so provider-specific SQL translation is exercised.
///
/// A database per factory, rather than one shared database, keeps the isolation the
/// SQLite mode gets for free: xUnit gives each test class its own ApiFactory, and no
/// class should be able to see another's rows.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Admin connection string for a PostgreSQL server, in Npgsql key/value form
    /// (<c>Host=...;Port=...;Database=...;Username=...;Password=...</c>) — not a
    /// <c>postgres://</c> URL. When set, tests run against that server instead of SQLite.
    /// The named database only has to exist so we can connect to it to issue CREATE
    /// DATABASE; the tests themselves never touch it.
    /// </summary>
    public const string TestDatabaseVariable = "TEST_DATABASE_URL";

    /// <summary>The secret tests send in the X-Agent-Secret header. Test-only value.</summary>
    public const string AgentSharedSecret = "test-agent-shared-secret";

    /// <summary>Log entries written during the test, for asserting on warnings.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    // SQLite mode only; null when running against PostgreSQL.
    private readonly SqliteConnection? _sqliteConnection;

    // PostgreSQL mode only; all null when running against SQLite.
    private readonly string? _adminConnectionString;
    private readonly string? _testConnectionString;
    private readonly string? _testDatabaseName;

    /// <summary>True when this factory is backed by a real PostgreSQL database.</summary>
    public bool UsesPostgres => _testConnectionString is not null;

    public ApiFactory()
    {
        // Configuration is supplied through environment variables because Program.cs reads
        // these values while the builder is still being constructed, before any
        // ConfigureAppConfiguration hook would run. The connection string here is never
        // connected to — ConfigureWebHost replaces the DbContext registration below — but
        // Program.cs throws at startup if it is absent.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=unused-in-tests");
        Environment.SetEnvironmentVariable("Jwt__Secret", "test-signing-key-that-is-definitely-long-enough");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "CampusFacilities.Api.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "CampusFacilities.Tests");
        Environment.SetEnvironmentVariable("Agent__SharedSecret", AgentSharedSecret);

        var adminConnectionString = Environment.GetEnvironmentVariable(TestDatabaseVariable);

        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            // An in-memory SQLite database exists only while a connection to it is open,
            // so this one is held open for the lifetime of the factory.
            _sqliteConnection = new SqliteConnection("DataSource=:memory:");
            _sqliteConnection.Open();
            return;
        }

        _adminConnectionString = adminConnectionString;

        // A Guid, not a test name: two factories must never collide, and the name has to
        // stay inside PostgreSQL's 63-byte identifier limit.
        _testDatabaseName = $"maintenx_test_{Guid.NewGuid():N}";
        _testConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = _testDatabaseName
        }.ConnectionString;

        CreateTestDatabase();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: this keeps the demo-data seeder switched off so each test
        // starts from an empty database and creates exactly the users it needs.
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        builder.ConfigureServices(services =>
        {
            // The workflow runner is a background writer. Left in, it would race with the
            // assertions of whichever test is running, and on SQLite it would additionally
            // be writing through the single shared connection below. Removed in both modes
            // so a test behaves identically on a laptop and in CI; what the POST test
            // actually asserts is the hand-off, by reading the id straight off
            // IWorkflowQueue.
            //
            // The timetable sync worker goes for the same reason: a timer writing
            // ClassScheduleSlot rows underneath a test's assertions. Tests call the sync
            // through POST /api/timetable/sync instead. The verification sweep likewise —
            // it runs a pass at startup, which would move a test's Pending checks before
            // the test looked at them. Tests call ProcessDueChecksAsync or
            // POST /api/workflows/verification-sweep. And the verification agent runner — it
            // would judge a test's queued checks behind its back; tests call
            // IVerificationAgentService.JudgeQueuedChecksAsync.
            foreach (var worker in services
                .Where(d => d.ImplementationType == typeof(WorkflowRunner)
                         || d.ImplementationType == typeof(TimetableSyncWorker)
                         || d.ImplementationType == typeof(VerificationSweepService)
                         || d.ImplementationType == typeof(VerificationAgentRunner))
                .ToList())
            {
                services.Remove(worker);
            }

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();

            if (_testConnectionString is not null)
            {
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_testConnectionString));
            }
            else
            {
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_sqliteConnection!));
            }

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (_testConnectionString is not null)
            {
                // Migrate, not EnsureCreated. This is the run that proves the files in
                // api/Data/Migrations actually apply to a real PostgreSQL server, which is
                // the one thing the SQLite mode structurally cannot check.
                db.Database.Migrate();
            }
            else
            {
                // The migrations are Npgsql-specific SQL, so SQLite builds from the model.
                db.Database.EnsureCreated();
            }
        });
    }

    private void CreateTestDatabase()
    {
        using var connection = new NpgsqlConnection(_adminConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        // A database name cannot be a parameter. The value is a Guid this class generated,
        // never caller input, and it is quoted as an identifier.
        command.CommandText = $"CREATE DATABASE \"{_testDatabaseName}\"";
        command.ExecuteNonQuery();
    }

    private void DropTestDatabase()
    {
        // Npgsql pools connections, and a pooled connection to the test database keeps it
        // "being accessed by other users" long after the host has been disposed. Clearing
        // the pool first, and FORCE as a backstop, makes the drop reliable.
        NpgsqlConnection.ClearAllPools();

        using var connection = new NpgsqlConnection(_adminConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_testDatabaseName}\" WITH (FORCE)";
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------------------
    // Accounts. POST /api/auth/register creates a Reporter for anyone, and every other role
    // only for an Admin — so a test that needs a Technician or a FacilitiesManager registers
    // it AS AN ADMIN, through the real endpoint. The one account nothing can register is the
    // first Admin itself; that one is written straight into the table, the way a deployment's
    // first Admin is seeded, and signed in through the real login.
    // ---------------------------------------------------------------------------

    private const string BootstrapAdminEmail = "bootstrap-admin@tests.invalid";
    private const string BootstrapAdminPassword = "BootstrapAdmin1";

    private static readonly JsonSerializerOptions AccountJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _adminLock = new(1, 1);
    private string? _adminToken;

    /// <summary>
    /// POST /api/auth/register as the bootstrap Admin, so the request may ask for any role.
    /// The response is the endpoint's own: a 201 carrying the NEW account's token.
    /// </summary>
    public async Task<HttpResponseMessage> RegisterAsync(RegisterRequest request)
    {
        using var admin = await CreateAdminClientAsync();
        return await admin.PostAsJsonAsync("/api/auth/register", request, AccountJson);
    }

    /// <summary>A client signed in as the bootstrap Admin — for the registry, buildings and rooms.</summary>
    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await BootstrapAdminTokenAsync());
        return client;
    }

    private async Task<string> BootstrapAdminTokenAsync()
    {
        await _adminLock.WaitAsync();

        try
        {
            if (_adminToken is not null)
            {
                return _adminToken;
            }

            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (!await db.Users.AnyAsync(u => u.Email == BootstrapAdminEmail))
                {
                    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
                    var admin = new User { Email = BootstrapAdminEmail, FullName = "Bootstrap Admin", Role = Role.Admin };
                    admin.PasswordHash = hasher.HashPassword(admin, BootstrapAdminPassword);

                    db.Users.Add(admin);
                    await db.SaveChangesAsync();
                }
            }

            using var anonymous = CreateClient();
            var login = await anonymous.PostAsJsonAsync(
                "/api/auth/login", new LoginRequest(BootstrapAdminEmail, BootstrapAdminPassword), AccountJson);
            login.EnsureSuccessStatusCode();

            _adminToken = (await login.Content.ReadFromJsonAsync<AuthResponse>(AccountJson))!.Token;
            return _adminToken;
        }
        finally
        {
            _adminLock.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Disposes the host first, which disposes the service provider and with it every
        // DbContext still holding a connection to the database being dropped below.
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        _sqliteConnection?.Dispose();

        if (_testDatabaseName is not null)
        {
            DropTestDatabase();
        }
    }
}
