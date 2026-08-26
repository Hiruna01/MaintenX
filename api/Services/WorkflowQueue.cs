using System.Threading.Channels;

namespace CampusFacilities.Api.Services;

/// <summary>
/// An in-process queue of workflow ids, backed by System.Threading.Channels.
///
/// Registered AddSingleton, and that is not the captive-dependency mistake the other
/// services avoid: this class holds a Channel and nothing else — no DbContext, no scoped
/// dependency of any kind. The runner opens its own DI scope per item.
///
/// In-process means a restart loses anything still queued. That is an accepted limitation
/// at this scope: the rows survive in Postgres in state Submitted, so a requeue-on-startup
/// sweep (or a real broker) can be added later without changing this interface.
/// </summary>
public class WorkflowQueue : IWorkflowQueue
{
    /// <summary>
    /// Bounded on purpose. If the runner ever falls this far behind, a producer waits
    /// rather than the queue growing until the process runs out of memory.
    /// </summary>
    private const int Capacity = 100;

    private readonly Channel<int> _channel = Channel.CreateBounded<int>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

    public ValueTask EnqueueAsync(int workflowId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(workflowId, cancellationToken);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
