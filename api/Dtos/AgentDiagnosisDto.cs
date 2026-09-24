namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — the diagnostic agent's last recorded answer for a report, read back out of
/// its AgentStep. The step's PayloadJson is the audit copy and is never edited; this is a
/// typed reading of it for a human deciding what to spend.
///
/// ADVICE, NEVER A DECISION. Confidence and RecommendedNextAction are STRINGS, not C# enums,
/// for the same reason as VerificationCheck.AgentOutcome: giving them an enum would imply
/// the system acts on them, and nothing does. Whether equipment is replaced is approval
/// routing, and that is ApprovalBasisDto.
///
/// When the agent ran but failed, ValidationResult says so and ErrorMessage says why, and
/// the diagnosis fields are empty — there is no such thing as an empty diagnosis, so none is
/// invented. <see cref="OutputReadable"/> is false when a step says Ok but its payload could
/// not be read into this shape; the raw payload is still on the step.
/// </summary>
public record AgentDiagnosisDto(
    int StepId,

    // Which run produced it: a report can be run more than once, and a diagnosis from an
    // older run must be tellable from the current one.
    int WorkflowId,
    DateTime RecordedAt,

    // "Ok" or "SafeFailure", verbatim from the step.
    string? ValidationResult,
    string? ErrorMessage,
    bool OutputReadable,

    // Most likely first is NOT guaranteed — the agent's own order, with the one it rates
    // primary named by PrimaryHypothesisIndex.
    IReadOnlyList<DiagnosisHypothesisDto> Hypotheses,
    int? PrimaryHypothesisIndex,

    // inspect / repair / replace / monitor, as the agent wrote it.
    string? RecommendedNextAction,

    // At most 400 characters. Not a message: there is no reply to it.
    string? ReasoningSummary);

/// <summary>
/// One candidate cause. Every hypothesis carries at least one evidence item — the agent's
/// schema refuses a cause standing on nothing — and the evidence is shown verbatim, since it
/// is where the agent cites the dated service visits it is going on.
/// </summary>
public record DiagnosisHypothesisDto(
    string Cause,

    // high / medium / low, as the agent wrote it.
    string Confidence,

    IReadOnlyList<string> Evidence);
