namespace CampusFacilities.Api.Services;

/// <summary>
/// "There may be a check for the verification agent now" — sent by a reporter's answer and by
/// the sweep, heard by VerificationAgentRunner, so a demo does not wait a sweep interval for a
/// verdict.
///
/// A doorbell, not a queue. It carries nothing: the rows are the queue (AgentQueuedAt), and a
/// wake the runner misses — a restart, say — costs only the wait until its next timed pass.
/// Ringing it twice before the runner looks is one wake.
///
/// It exists so that no REQUEST calls the agent. A pass can take minutes per check, and a
/// controller action that waited on one would be the synchronous agent call CLAUDE.md forbids.
/// </summary>
public interface IVerificationAgentSignal
{
    /// <summary>Wakes the runner. Never blocks and never throws.</summary>
    void Wake();

    /// <summary>
    /// Waits until woken or until <paramref name="timeout"/> passes. True when woken. Used by
    /// the runner between passes, and by tests to see whether something rang.
    /// </summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
