using System.Threading.Channels;

namespace CampusFacilities.Api.Services;

/// <summary>
/// A Channel of capacity one that drops a second write: however often it is rung before the
/// runner looks, the runner wakes once. A singleton, safely — it holds a Channel and nothing
/// else, like WorkflowQueue.
/// </summary>
public class VerificationAgentSignal : IVerificationAgentSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Wake() => _channel.Writer.TryWrite(true);

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_channel.Reader.TryRead(out _))
        {
            return true;
        }

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);

        try
        {
            await _channel.Reader.ReadAsync(timer.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout, not a shutdown: time for a timed pass.
            return false;
        }
    }
}
