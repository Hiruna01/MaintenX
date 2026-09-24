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
var agentSettings = new AgentSettings
{
    SharedSecret = builder.Configuration["Agent:SharedSecret"]
        ?? builder.Configuration["AGENT_SHARED_SECRET"]
        ?? string.Empty,
    BaseUrl = builder.Configuration["Agent:BaseUrl"]
        ?? builder.Configuration["AGENT_SERVICE_URL"]
        ?? string.Empty,
    TimeoutSeconds = builder.Configuration.GetValue<double?>("Agent:TimeoutSeconds")
        ?? builder.Configuration.GetValue<double?>("AGENT_TIMEOUT_SECONDS")
        ?? 60
};

builder.Services.AddSingleton(agentSettings);

// ---------------------------------------------------------------------------
// Approval routing
//
// The cost above which a work order needs a manager's decision. From configuration, never
// a literal in a service — see ApprovalSettings for why. Falls back to the
// APPROVAL_COST_THRESHOLD name used by the root .env.example, then to the class's own
// default, so the number is written down in exactly one place.
// ---------------------------------------------------------------------------
var approvalSettings = new ApprovalSettings
{
    CostThreshold = builder.Configuration.GetValue<decimal?>("Approval:CostThreshold")
        ?? builder.Configuration.GetValue<decimal?>("APPROVAL_COST_THRESHOLD")
        ?? ApprovalSettings.DefaultCostThreshold
};

// A negative threshold is meaningless and a zero one sends every trivial job to a manager,
// which is how an approval step gets ignored. Fail at startup with a clear message rather
// than quietly routing every work order — or none of them — for the life of the deployment.
if (approvalSettings.CostThreshold <= 0)
{
    throw new InvalidOperationException(
        "The work order approval threshold (Approval:CostThreshold / APPROVAL_COST_THRESHOLD) " +
        "must be greater than zero.");
}

builder.Services.AddSingleton(approvalSettings);

// ---------------------------------------------------------------------------
// Verification
//
// How long after a work order completes before the reporter is asked whether the repair
// held, and how often the sweep looks for checks that have come due. From configuration,
// never literals in a service — see VerificationSettings. Falls back to the VERIFICATION_*
// names used by the root .env.example, then to the class's own defaults.
// ---------------------------------------------------------------------------
var verificationSettings = new VerificationSettings
{
    DelayDays = builder.Configuration.GetValue<int?>("Verification:DelayDays")
        ?? builder.Configuration.GetValue<int?>("VERIFICATION_DELAY_DAYS")
        ?? VerificationSettings.DefaultDelayDays,
    SweepIntervalMinutes = builder.Configuration.GetValue<int?>("Verification:SweepIntervalMinutes")
        ?? builder.Configuration.GetValue<int?>("VERIFICATION_SWEEP_INTERVAL_MINUTES")
        ?? VerificationSettings.DefaultSweepIntervalMinutes,
    ResponseWindowDays = builder.Configuration.GetValue<int?>("Verification:ResponseWindowDays")
        ?? builder.Configuration.GetValue<int?>("VERIFICATION_RESPONSE_WINDOW_DAYS")
        ?? VerificationSettings.DefaultResponseWindowDays
};

// A zero or negative delay defeats the whole component: asked the same afternoon, every
// reporter says yes, and the confirmation rate becomes a number that always reads well and
// means nothing. A zero sweep interval would spin. Fail at startup with a clear message.
if (verificationSettings.DelayDays <= 0)
{
    throw new InvalidOperationException(
        "The verification delay (Verification:DelayDays / VERIFICATION_DELAY_DAYS) must be " +
        "greater than zero — a check with no delay confirms faults that have not had time to recur.");
}

if (verificationSettings.SweepIntervalMinutes <= 0)
{
    throw new InvalidOperationException(
        "The verification sweep interval (Verification:SweepIntervalMinutes / " +
        "VERIFICATION_SWEEP_INTERVAL_MINUTES) must be greater than zero.");
}

// Zero would hand every check to the agent the moment the reporter was asked, before they
// could possibly have answered — the agent would only ever see silence.
if (verificationSettings.ResponseWindowDays <= 0)
{
    throw new InvalidOperationException(
        "The verification response window (Verification:ResponseWindowDays / " +
        "VERIFICATION_RESPONSE_WINDOW_DAYS) must be greater than zero.");
}

builder.Services.AddSingleton(verificationSettings);

// ---------------------------------------------------------------------------
// Scheduling
//
// The campus working day, the time zone it is measured in, and the clear time required
// either side of a class. From configuration, never literals in a service — see
// SchedulingSettings. Falls back to the SCHEDULING_* names used by the root .env.example,
// then to the class's own defaults. A blank value counts as unset.
// ---------------------------------------------------------------------------
string? FirstSet(params string[] keys) =>
    keys.Select(key => builder.Configuration[key]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

var schedulingSettings = new SchedulingSettings
{
    TimeZoneId = FirstSet("Scheduling:TimeZone", "SCHEDULING_TIME_ZONE")
        ?? SchedulingSettings.DefaultTimeZoneId,
    WorkdayStart = FirstSet("Scheduling:WorkdayStart", "SCHEDULING_WORKDAY_START") is { } start
        ? TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture)
        : SchedulingSettings.DefaultWorkdayStart,
    WorkdayEnd = FirstSet("Scheduling:WorkdayEnd", "SCHEDULING_WORKDAY_END") is { } end
        ? TimeOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)
        : SchedulingSettings.DefaultWorkdayEnd,
    ClassBufferMinutes = FirstSet("Scheduling:ClassBufferMinutes", "SCHEDULING_CLASS_BUFFER_MINUTES") is { } buffer
        ? int.Parse(buffer, System.Globalization.CultureInfo.InvariantCulture)
        : SchedulingSettings.DefaultClassBufferMinutes
};

// Resolved now rather than on the first slot search, so a misspelt zone stops the API
// booting with a clear message instead of turning every availability request into a 500.
try
{
    _ = schedulingSettings.TimeZone;
}
catch (TimeZoneNotFoundException)
{
    throw new InvalidOperationException(
        $"Unknown scheduling time zone '{schedulingSettings.TimeZoneId}' (Scheduling:TimeZone / " +
        "SCHEDULING_TIME_ZONE). Use an IANA id such as Asia/Colombo.");
}

if (schedulingSettings.WorkdayEnd <= schedulingSettings.WorkdayStart)
{
    throw new InvalidOperationException(
        "The scheduling working day must end after it starts (Scheduling:WorkdayStart / WorkdayEnd).");
}

if (schedulingSettings.ClassBufferMinutes < 0)
{
    throw new InvalidOperationException(
        "The class buffer (Scheduling:ClassBufferMinutes / SCHEDULING_CLASS_BUFFER_MINUTES) " +
        "cannot be negative.");
}

builder.Services.AddSingleton(schedulingSettings);

// ---------------------------------------------------------------------------
// Photo storage (Supabase Storage)
//
// Falls back to the SUPABASE_* names used by the root .env.example. Like the agent settings,
// an unset value does not stop the API booting — photo uploads return 503 instead, and a
// warning below says so once. Locally these go in dotnet user-secrets, never in a file.
// ---------------------------------------------------------------------------
var storageSettings = new StorageSettings
{
    Url = builder.Configuration["Supabase:Url"]
        ?? builder.Configuration["SUPABASE_URL"]
        ?? string.Empty,
    ServiceKey = builder.Configuration["Supabase:ServiceKey"]
        ?? builder.Configuration["SUPABASE_SERVICE_KEY"]
        ?? string.Empty,
    Bucket = builder.Configuration["Supabase:StorageBucket"]
        ?? builder.Configuration["SUPABASE_STORAGE_BUCKET"]
        ?? StorageSettings.DefaultBucket
};

builder.Services.AddSingleton(storageSettings);

// ---------------------------------------------------------------------------
// Campus timetable (Google Calendar, read as a service account)
//
// Falls back to the GOOGLE_* names used by the root .env.example. Like the agent and storage
// settings, an unset value does not stop the API booting — a sync then reports itself
// degraded with NotConfigured, and the slot finder reads whatever classes are cached. The
// key is base64 of the downloaded JSON file; see GoogleCalendarSettings for why. Locally it
// goes in dotnet user-secrets, on Render in an environment variable, never in a file.
//
// A value that IS set but is not a service account key stops startup, the same as a
// misspelt time zone: pasting the raw JSON instead of its base64, or an OAuth client secret
// instead of a service account key, is a mistake better found now than at the first sync.
// ---------------------------------------------------------------------------
var googleJsonBase64 = FirstSet("Google:ServiceAccountJsonBase64", "GOOGLE_SERVICE_ACCOUNT_JSON_BASE64");
string googleServiceAccountJson = string.Empty;
string? googleServiceAccountEmail = null;

if (googleJsonBase64 is not null)
{
    try
    {
        googleServiceAccountJson = Encoding.UTF8.GetString(Convert.FromBase64String(googleJsonBase64.Trim()));
    }
    catch (FormatException)
    {
        throw new InvalidOperationException(
            "Google:ServiceAccountJsonBase64 / GOOGLE_SERVICE_ACCOUNT_JSON_BASE64 is not base64. " +
            "Set it to the base64 of the service account key file (base64 -i key.json), not the JSON itself.");
    }

    try
    {
        googleServiceAccountEmail = GoogleCalendarClient.CreateCredential(googleServiceAccountJson).Id;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            "Google:ServiceAccountJsonBase64 / GOOGLE_SERVICE_ACCOUNT_JSON_BASE64 does not decode to a " +
            "service account key. Download a JSON key from IAM & Admin → Service Accounts → Keys.", ex);
    }
}

var googleCalendarSettings = new GoogleCalendarSettings
{
    CalendarId = FirstSet("Google:CalendarId", "GOOGLE_CALENDAR_ID") ?? string.Empty,
    ServiceAccountJson = googleServiceAccountJson,
    TimeoutSeconds = FirstSet("Google:TimeoutSeconds", "GOOGLE_CALENDAR_TIMEOUT_SECONDS") is { } googleTimeout
        ? double.Parse(googleTimeout, System.Globalization.CultureInfo.InvariantCulture)
        : GoogleCalendarSettings.DefaultTimeoutSeconds,
    SyncIntervalMinutes = FirstSet("Google:SyncIntervalMinutes", "TIMETABLE_SYNC_INTERVAL_MINUTES") is { } syncInterval
        ? int.Parse(syncInterval, System.Globalization.CultureInfo.InvariantCulture)
        : GoogleCalendarSettings.DefaultSyncIntervalMinutes
};

// A zero timeout fails every sync; a zero interval spins. Same rule as the verification sweep.
if (googleCalendarSettings.TimeoutSeconds <= 0 || googleCalendarSettings.SyncIntervalMinutes <= 0)
{
    throw new InvalidOperationException(
        "Google:TimeoutSeconds and Google:SyncIntervalMinutes must both be greater than zero.");
}

builder.Services.AddSingleton(googleCalendarSettings);

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

// Reports
builder.Services.AddScoped<IReportService, ReportService>();

// Asset registry. The failure summary reads "today" from TimeProvider rather than
// DateTime.UtcNow, so its date boundaries can be pinned in tests.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAssetService, AssetService>();

// Clarification questions and answers
builder.Services.AddScoped<IClarificationService, ClarificationService>();

// Verification — did the repair actually hold?
builder.Services.AddScoped<IVerificationService, VerificationService>();

// Estate-wide metrics — counts and arithmetic only, no agent.
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

// The sweep on a timer — once at startup, then every Verification:SweepIntervalMinutes.
// A singleton like every hosted service, so it opens a scope per pass and resolves
// IVerificationService there. POST /api/verifications/run-sweep runs the same pass on demand.
builder.Services.AddHostedService<VerificationSweepService>();

// Work orders — the approval gate, assignment and completion. Completion raises the
// verification check above inside its own transaction.
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();

// Users by role — the technician picker behind assigning and filtering work orders.
builder.Services.AddScoped<IUserService, UserService>();

// File storage — report photos and completion photos, one path. Scoped like the services
// above; it takes its HttpClient from the factory per upload, so the handler is pooled
// rather than a new HttpClient being built for every photo.
builder.Services.AddHttpClient(SupabaseStorageService.HttpClientName, client =>
{
    // A stalled upload must not hold the request open indefinitely. The service turns the
    // resulting timeout into a 503.
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IFileStorageService, SupabaseStorageService>();

// Campus timetable. The sync is scoped (it writes through AppDbContext); the Google client
// is a singleton, like the workflow queue, because it holds no DbContext and keeping one
// CalendarService reuses the service account's access token instead of fetching a new one
// per sync. The worker runs the sync on a timer; POST /api/timetable/sync runs it on demand.
// The slot finder in WorkOrderService reads ClassScheduleSlot and never calls Google.
builder.Services.AddSingleton<IGoogleCalendarClient, GoogleCalendarClient>();
builder.Services.AddScoped<ITimetableSyncService, GoogleCalendarSyncService>();
builder.Services.AddHostedService<TimetableSyncWorker>();

// Auth
builder.Services.AddScoped<IAuthService, AuthService>();

// Agent workflows
builder.Services.AddScoped<IWorkflowService, WorkflowService>();

// The queue holds a Channel<int> and nothing else — no DbContext, no scoped dependency —
// so unlike the services above it is genuinely safe as a singleton. It has to be one:
// the controller and the background runner must see the same queue.
builder.Services.AddSingleton<IWorkflowQueue, WorkflowQueue>();

// The API's outbound call to the agent service. A typed HttpClient, so the timeout and
// base address are configured once here rather than at every call site, and the handler
// is pooled instead of a new HttpClient being constructed per workflow.
//
// Registered as a typed client (transient), not a singleton: it holds no DbContext, and
// the background runner resolves it from the scope it opens per workflow.
builder.Services.AddHttpClient<IAgentClient, AgentClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(agentSettings.BaseUrl))
    {
        client.BaseAddress = new Uri(agentSettings.BaseUrl);
    }

    // The runner must never wait forever on a wedged agent. AgentClient turns the
    // resulting TaskCanceledException into a plain failure result.
    client.Timeout = TimeSpan.FromSeconds(agentSettings.TimeoutSeconds);
});

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
if (string.IsNullOrEmpty(agentSettings.SharedSecret))
{
    app.Logger.LogWarning(
        "No agent shared secret configured (Agent:SharedSecret / AGENT_SHARED_SECRET). " +
        "Every call to /api/internal/tools/* will be rejected with 401.");
}

// Same reasoning in the other direction: without a URL the runner cannot call the agent,
// so every workflow it picks up will end in Failed. Say so once rather than leaving it to
// be discovered one failed workflow at a time.
if (string.IsNullOrEmpty(agentSettings.BaseUrl))
{
    app.Logger.LogWarning(
        "No agent service URL configured (Agent:BaseUrl / AGENT_SERVICE_URL). " +
        "Every workflow the background runner picks up will fail.");
}

if (!storageSettings.IsConfigured)
{
    app.Logger.LogWarning(
        "No Supabase Storage configured (Supabase:Url and Supabase:ServiceKey / SUPABASE_URL " +
        "and SUPABASE_SERVICE_KEY). Every photo upload will be refused with 503.");
}

// Without a timetable the slot finder sees no classes and offers every room as free. Say so
// once; when it IS configured, say which address the calendar has to be shared with, which
// is the setup step most easily missed.
if (!googleCalendarSettings.IsConfigured)
{
    app.Logger.LogWarning(
        "No Google Calendar configured (Google:CalendarId and Google:ServiceAccountJsonBase64 / " +
        "GOOGLE_CALENDAR_ID and GOOGLE_SERVICE_ACCOUNT_JSON_BASE64). The timetable will not sync, " +
        "and the slot finder will only see classes already in the database.");
}
else
{
    app.Logger.LogInformation(
        "Timetable sync reads Google Calendar {CalendarId} as {ServiceAccountEmail}. " +
        "The calendar must be shared with that address.",
        googleCalendarSettings.CalendarId, googleServiceAccountEmail);
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
        await DbSeeder.SeedAsync(
            db, app.Configuration, passwordHasher, verificationSettings, logger);
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
