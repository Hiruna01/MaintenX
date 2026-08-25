using System.Text;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Middleware;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging (Serilog)
// ---------------------------------------------------------------------------
// Serilog logs request METADATA only — method, path, status, duration. It never reads
// the request body, so passwords in a login or register payload are never written to a
// log sink. Do not add body logging here; see UseSerilogRequestLogging below.
//
// writeToProviders: true means log events also reach any ILoggerProvider registered in
// DI, instead of Serilog swallowing them. That is what lets an integration test assert
// "this call wrote a warning" — the allow-list rejection in InternalToolsController is
// a security control, so the log line is part of the behaviour under test, not decoration.
builder.Host.UseSerilog((context, loggerConfiguration) =>
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(),
    writeToProviders: true);

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
// Authentication (JWT bearer)
// Secret, issuer and audience all come from configuration — never a literal here.
// Falls back to the JWT_* names used by the root .env.example.
// ---------------------------------------------------------------------------
var jwtSettings = new JwtSettings
{
    Secret = builder.Configuration["Jwt:Secret"]
        ?? builder.Configuration["JWT_SECRET"]
        ?? throw new InvalidOperationException(
            "No JWT signing secret configured. Set Jwt:Secret or the JWT_SECRET environment variable."),
    Issuer = builder.Configuration["Jwt:Issuer"]
        ?? builder.Configuration["JWT_ISSUER"]
        ?? throw new InvalidOperationException(
            "No JWT issuer configured. Set Jwt:Issuer or the JWT_ISSUER environment variable."),
    Audience = builder.Configuration["Jwt:Audience"]
        ?? builder.Configuration["JWT_AUDIENCE"]
        ?? throw new InvalidOperationException(
            "No JWT audience configured. Set Jwt:Audience or the JWT_AUDIENCE environment variable.")
};

// HMAC-SHA256 needs at least a 256-bit key. Fail at startup with a clear message rather
// than at the first login with an opaque one.
if (Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
{
    throw new InvalidOperationException(
        "The JWT signing secret must be at least 32 characters (256 bits) for HMAC-SHA256.");
}

builder.Services.AddSingleton(jwtSettings);

// ---------------------------------------------------------------------------
// Agent service (machine-to-machine)
//
// The Python agent authenticates to /api/internal/tools/* with a shared secret header,
// not a JWT — there is no user behind those calls and no role to check. Falls back to the
// AGENT_SHARED_SECRET name used by the root .env.example.
//
// Unlike the JWT settings this does not throw when unset: the API must still boot for
// team members who are not working on the agent. It fails CLOSED instead — an empty
// secret makes AgentSecretFilter reject every call — and warns loudly at startup below.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(new AgentSettings
{
    SharedSecret = builder.Configuration["Agent:SharedSecret"]
        ?? builder.Configuration["AGENT_SHARED_SECRET"]
        ?? string.Empty
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the claim names exactly as issued: sub, email, role. Without this, the
        // handler helpfully renames them to long WS-Federation URIs and lookups by "sub" fail.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateLifetime = true,
            // Default is a 5-minute grace period; expiry should mean expiry.
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.Email,
            RoleClaimType = "role"
        };
    });

// ---------------------------------------------------------------------------
// Authorization — one policy per Role enum member, so a typo is a compile error
// rather than a policy that silently never matches.
// ---------------------------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    foreach (var role in Enum.GetNames<Role>())
    {
        options.AddPolicy(role, policy => policy.RequireRole(role));
    }
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

// Auth
builder.Services.AddScoped<IAuthService, AuthService>();

// Agent workflows
builder.Services.AddScoped<IWorkflowService, WorkflowService>();

// The queue holds a Channel<int> and nothing else — no DbContext, no scoped dependency —
// so unlike the services above it is genuinely safe as a singleton. It has to be one:
// the controller and the background runner must see the same queue.
builder.Services.AddSingleton<IWorkflowQueue, WorkflowQueue>();

// The background half of "POST /api/workflows returns 202". Registered here so the host
// starts it at boot; it opens its own DI scope per workflow.
builder.Services.AddHostedService<WorkflowRunner>();

// Applied to InternalToolsController with [ServiceFilter], which needs the filter itself
// in the container so it can be constructed with its dependencies injected.
builder.Services.AddScoped<AgentSecretFilter>();

// The password hasher is stateless and thread-safe and holds no DbContext, so unlike the
// services above it is genuinely safe as a singleton.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// ---------------------------------------------------------------------------
// MVC + Swagger
// ---------------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Send and accept enums by NAME, so the JSON contract reads {"role":"FacilitiesManager"}
        // rather than {"role":2}. This matches how the database and the JWT role claim store
        // it, and means the React and Flutter clients never hardcode magic numbers whose
        // meaning would silently change if a new Role member were inserted in the middle.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Puts an Authorize button in the Swagger UI so a token can be pasted in during a demo.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by /api/auth/login (no Bearer prefix needed)."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------------------

// A missing agent secret is not fatal, but it does silently disable the agent's only way
// into this API, so say so once at startup rather than leaving someone to debug 401s.
if (string.IsNullOrEmpty(app.Services.GetRequiredService<AgentSettings>().SharedSecret))
{
    app.Logger.LogWarning(
        "No agent shared secret configured (Agent:SharedSecret / AGENT_SHARED_SECRET). " +
        "Every call to /api/internal/tools/* will be rejected with 401.");
}

// First in the pipeline so it wraps everything after it.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    // RequestPath excludes the query string, and nothing here touches the body, so a
    // password can never reach a log line. Only add fields to this list that are safe
    // to write to a log sink in plain text.
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Clients");

// Authentication must come before authorization: work out WHO the caller is, then
// decide WHAT they may do. Reversed, every [Authorize] endpoint returns 401.
app.UseAuthentication();
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
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DbSeeder");

    try
    {
        await DbSeeder.SeedAsync(db, app.Configuration, passwordHasher, logger);
    }
    catch (Exception ex)
    {
        // Most often the migration has not been applied yet. Log and keep going so the
        // API still starts and /health and Swagger stay reachable.
        logger.LogError(ex, "Seeding failed. Have you run 'dotnet ef database update'?");
    }
}

app.Run();

// Exposed so the xUnit project can boot this exact pipeline through WebApplicationFactory.
public partial class Program { }
