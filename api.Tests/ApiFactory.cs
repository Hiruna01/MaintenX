using CampusFacilities.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public ApiFactory()
    {
        // Configuration is supplied through environment variables because Program.cs reads
        // these values while the builder is still being constructed, before any
        // ConfigureAppConfiguration hook would run.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=unused-in-tests");
        Environment.SetEnvironmentVariable("Jwt__Secret", "test-signing-key-that-is-definitely-long-enough");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "CampusFacilities.Api.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "CampusFacilities.Tests");

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

        builder.ConfigureServices(services =>
        {
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
