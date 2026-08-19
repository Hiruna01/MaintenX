using CampusFacilities.Api.Data;
using CampusFacilities.Api.Middleware;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging (Serilog)
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((context, loggerConfiguration) =>
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console());

// ---------------------------------------------------------------------------
// Database (EF Core + Npgsql)
// Connection string comes from configuration — never hardcoded.
// ---------------------------------------------------------------------------
// Falls back to DATABASE_URL so the root .env.example works as documented.
// Either way the value must be in Npgsql key/value form:
//   Host=...;Port=5432;Database=...;Username=...;Password=...
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException(
        "No database connection string configured. Set ConnectionStrings:DefaultConnection " +
        "(user secrets or appsettings.Development.json) or the DATABASE_URL environment variable.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ---------------------------------------------------------------------------
// CORS — origins come from configuration (Cors:AllowedOrigins).
// ---------------------------------------------------------------------------
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Clients", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ---------------------------------------------------------------------------
// Application services
//
// AddScoped, never AddSingleton: these services depend on AppDbContext, which is
// scoped. A singleton holding a scoped DbContext is a captive dependency bug.
// Add one line per component below, grouped by feature owner.
// ---------------------------------------------------------------------------

// Buildings
builder.Services.AddScoped<IBuildingService, BuildingService>();

// Rooms
builder.Services.AddScoped<IRoomService, RoomService>();

// ---------------------------------------------------------------------------
// MVC + Swagger
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------------------

// First in the pipeline so it wraps everything after it.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Clients");

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    utcTime = DateTime.UtcNow
}));

// ---------------------------------------------------------------------------
// Development-only demo data. Idempotent — safe to run on every start.
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DbSeeder");

    try
    {
        await DbSeeder.SeedAsync(db, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        // Most often the migration has not been applied yet. Log and keep going so the
        // API still starts and /health and Swagger stay reachable.
        logger.LogError(ex, "Seeding failed. Have you run 'dotnet ef database update'?");
    }
}

app.Run();
