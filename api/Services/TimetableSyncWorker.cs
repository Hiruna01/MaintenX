namespace CampusFacilities.Api.Services;

/// <summary>
/// Runs the timetable sync on a timer — once at startup, then every
/// <see cref="GoogleCalendarSettings.SyncIntervalMinutes"/>. POST /api/timetable/sync runs
/// the same sync on demand, so a demo never has to wait for this.
///
/// Same shape as WorkflowRunner: a singleton hosted service that holds no DbContext, opens
/// a DI scope per run and hands the work to the scoped service. The sync body lives in
/// GoogleCalendarSyncService, where it can be tested without starting a background worker.
/// </summary>
public class TimetableSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GoogleCalendarSettings _settings;
    private readonly ILogger<TimetableSyncWorker> _logger;

    public TimetableSyncWorker(
        IServiceScopeFactory scopeFactory,
        GoogleCalendarSettings settings,
        ILogger<TimetableSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.IsConfigured)
        {
            // Said once here rather than as a degraded-sync warning every hour. The manual
            // endpoint still answers, with FailureReason NotConfigured.
            _logger.LogInformation("Scheduled timetable sync is off: Google Calendar is not configured.");
            return;
        }

        // Hand control back to the host so a slow first sync cannot hold up startup.
        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.SyncIntervalMinutes));

        try
        {
            do
            {
                await SyncOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown, not a fault.
        }
    }

    private async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<ITimetableSyncService>();

            // A Google failure is already a degraded result, logged by the service. The
            // staleness warning is repeated here because nobody reads a timer's response.
            var result = await sync.SyncAsync(stoppingToken);

            if (result.IsStale)
            {
                _logger.LogWarning("Scheduled timetable sync: {Warning}", result.StalenessWarning);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database, not Google — the service never throws for a Google failure.
            // Logged and survived: the next tick tries again.
            _logger.LogError(ex, "Scheduled timetable sync failed.");
        }
    }
}
