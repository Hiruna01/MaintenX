using CampusFacilities.Api.Data;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace api.Tests;

/// <summary>
/// Boots the real Program.cs pipeline in memory, with the one thing tests must not need
/// swapped out: PostgreSQL becomes a SQLite in-memory database.
///
/// SQLite rather than the EF in-memory provider on purpose — the in-memory provider does
/// not enforce unique indexes, so the duplicate-email test would pass there even if the
/// index were missing. SQLite enforces it, so the test proves something real.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    /// <summary>The secret tests send in the X-Agent-Secret header. Test-only value.</summary>
    public const string AgentSharedSecret = "test-agent-shared-secret";

    /// <summary>Log entries written during the test, for asserting on warnings.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    public ApiFactory()
    {
        // Configuration is supplied through environment variables because Program.cs reads
        // these values while the builder is still being constructed, before any
        // ConfigureAppConfiguration hook would run.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=unused-in-tests");
        Environment.SetEnvironmentVariable("Jwt__Secret", "test-signing-key-that-is-definitely-long-enough");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "CampusFacilities.Api.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "CampusFacilities.Tests");
        Environment.SetEnvironmentVariable("Agent__SharedSecret", AgentSharedSecret);

        // An in-memory SQLite database exists only while a connection to it is open,
        // so this one is held open for the lifetime of the factory.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: this keeps the demo-data seeder switched off so each test
        // starts from an empty database and creates exactly the users it needs.
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        builder.ConfigureServices(services =>
        {
            // The workflow runner is a background writer, and every DbContext here shares
            // the one SQLite connection below — a runner writing while a test request
            // reads would make results depend on timing. It is removed so tests are
            // deterministic; what the POST test actually asserts is the hand-off, by
            // reading the id straight off IWorkflowQueue.
            var runner = services.SingleOrDefault(d => d.ImplementationType == typeof(WorkflowRunner));
            if (runner is not null)
            {
                services.Remove(runner);
            }

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
