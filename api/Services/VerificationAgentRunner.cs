namespace CampusFacilities.Api.Services;

/// <summary>
/// Drains the verification agent's queue — checks with AgentQueuedAt set and not judged since.
/// One pass at startup, then another whenever something rings IVerificationAgentSignal (a
/// reporter's answer, a sweep) or, failing that, every VerificationSettings.SweepIntervalMinutes
/// — which is also how a call that failed is retried.
///
/// Same shape as WorkflowRunner, TimetableSyncWorker and VerificationSweepService: a singleton
/// holding no DbContext and no IVerificationAgentService — both are scoped — that opens one DI
/// scope per pass. The pass itself is VerificationAgentService.JudgeQueuedChecksAsync, tested
/// without this class; tests remove this class from the container, like the other three.
/// </summary>
public class VerificationAgentRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVerificationAgentSignal _signal;
    private readonly VerificationSettings _settings;
    private readonly ILogger<VerificationAgentRunner> _logger;

    public VerificationAgentRunner(
        IServiceScopeFactory scopeFactory,
        IVerificationAgentSignal signal,
        VerificationSettings settings,
        ILogger<VerificationAgentRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Verification agent runner started.");

        // Hand control back to the host so a slow first pass cannot hold up startup.
        await Task.Yield();

        var interval = TimeSpan.FromMinutes(_settings.SweepIntervalMinutes);

        try
        {
            // A pass at startup: the rows are the queue, so whatever was waiting when the
            // process last stopped is still there.
            do
            {
                await PassOnceAsync(stoppingToken);
                await _signal.WaitAsync(interval, stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown, not a fault.
        }

        _logger.LogInformation("Verification agent runner stopped.");
    }

    private async Task PassOnceAsync(CancellationToken stoppingToken)
    {
        // THE LOOP MUST NEVER DIE — a bad row is caught inside the pass; this catches the rest
        // (the database unreachable), and the next wake or tick tries again.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IVerificationAgentService>()
                .JudgeQueuedChecksAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Verification agent pass failed; retrying on the next wake or tick.");
        }
    }
}
