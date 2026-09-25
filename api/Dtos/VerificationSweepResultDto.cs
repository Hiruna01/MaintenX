namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for POST /api/workflows/verification-sweep — what one pass of the
/// verification sweep did. The timer runs the same pass and logs it; nobody reads a timer's
/// response.
///
/// The counts are disjoint. A check asked in this pass cannot also be queued in it, because
/// the response window is measured from the moment it was asked; a workflow and a check are
/// different rows, so a repair that falls due is counted once in each of the first two.
/// </summary>
/// <param name="Processed">Every row this pass acted on: the sum of the four counts below.</param>
/// <param name="WorkflowsAwaitingVerification">
/// Completed workflows at least VerificationSettings.DelayDays old, moved to AwaitingVerification.
/// </param>
/// <param name="AskedReporter">Pending checks past their DueAt, moved to AwaitingReporterResponse.</param>
/// <param name="QueuedForAgent">Answered checks, and checks unanswered past the response window, handed to the verification agent.</param>
/// <param name="Failed">
/// Rows the pass threw on, each logged. A check the reporter had not answered was marked
/// Expired with the error as its ExpiredReason; a workflow was left Completed for the next
/// pass. The pass carried on with the rest.
/// </param>
public record VerificationSweepResultDto(
    int Processed,
    int WorkflowsAwaitingVerification,
    int AskedReporter,
    int QueuedForAgent,
    int Failed);
