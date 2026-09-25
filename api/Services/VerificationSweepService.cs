namespace CampusFacilities.Api.Services;

/// <summary>
/// Runs the verification sweep on a timer — once at startup, then every
/// <see cref="VerificationSettings.SweepIntervalMinutes"/> (Verification:SweepIntervalMinutes, or
/// VERIFICATION_SWEEP_INTERVAL_MINUTES).
/// POST /api/workflows/verification-sweep runs the same pass on demand, so a demo never
/// waits an hour for this. The pass moves Completed workflows DelayDays old to
/// AwaitingVerification and asks the reporter about their checks — see
/// IVerificationService.ProcessDueChecksAsync.
///
/// Same shape as WorkflowRunner and TimetableSyncWorker: a singleton hosted service that
/// holds no DbContext and no IVerificationService — both are scoped, and holding either
/// here would be the captive dependency the conventions warn about. It takes the scope
/// factory instead and opens one scope per pass. The sweep body lives in
/// VerificationService.ProcessDueChecksAsync, where it is tested without this class.
/// </summary>
public class VerificationSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VerificationSettings _settings;
    private readonly ILogger<VerificationSweepService> _logger;

    public VerificationSweepService(
        IServiceScopeFactory scopeFactory,
        VerificationSettings settings,
        ILogger<VerificationSweepService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Verification sweep started, every {Minutes} minute(s).", _settings.SweepIntervalMinutes);

        // Hand control back to the host so a slow first pass cannot hold up startup.
        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.SweepIntervalMinutes));

        try
        {
            // A pass at startup, not only after the first interval: on a host that sleeps
            // when idle, waking up is exactly when overdue checks have piled up.
            do
            {
                await SweepOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown, not a fault.
        }

        _logger.LogInformation("Verification sweep stopped.");
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        // THE LOOP MUST NEVER DIE. A bad row is already caught inside the pass; this catches
        // what is left — the database unreachable, a scope that cannot be built — so one
        // failed pass is a logged error and the next tick tries again, rather than an
        // exception that stops the sweep for the life of the process.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var verifications = scope.ServiceProvider.GetRequiredService<IVerificationService>();

            await verifications.ProcessDueChecksAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Verification sweep pass failed; retrying on the next tick.");
        }
    }
}
