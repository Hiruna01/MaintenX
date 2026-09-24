namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for POST /api/verifications/run-sweep — what one pass of the verification
/// sweep did. The timer runs the same pass and logs it; nobody reads a timer's response.
///
/// The three counts are disjoint. A check asked in this pass cannot also be queued in it,
/// because the response window is measured from the moment it was asked.
/// </summary>
/// <param name="Processed">Every check this pass acted on: the sum of the three counts below.</param>
/// <param name="AskedReporter">Pending checks past their DueAt, moved to AwaitingReporterResponse.</param>
/// <param name="QueuedForAgent">Answered checks, and checks unanswered past the response window, handed to the verification agent.</param>
/// <param name="Failed">
/// Checks the pass threw on. Each was logged and, if the reporter had not answered it,
/// marked Expired with the error as its ExpiredReason; the pass carried on with the rest.
/// </param>
public record VerificationSweepResultDto(
    int Processed,
    int AskedReporter,
    int QueuedForAgent,
    int Failed);
