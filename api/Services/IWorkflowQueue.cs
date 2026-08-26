namespace CampusFacilities.Api.Services;

/// <summary>
/// Hand-off between the controller thread and the background runner.
///
/// POST /api/workflows must not block on the agent service — an agent run takes tens of
/// seconds, and an HTTP request that waits for it ties up a thread and eventually times
/// out on the client. So the controller writes the row, drops the id in here, and returns
/// 202. The client polls GET /api/workflows/{id} for progress.
/// </summary>
public interface IWorkflowQueue
{
    /// <summary>Queues a workflow id for background processing. Never blocks.</summary>
    ValueTask EnqueueAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>Waits for the next queued workflow id. Used only by the background runner.</summary>
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
