using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — the ResolutionStrategist's last recorded proposal for a report, read back
/// out of its AgentStep. What the agent PROPOSED, which is not necessarily what the work
/// order says: the order is raised by a manager, who may cost or plan it differently. A
/// client shows the two side by side and lets the reader see any difference.
///
/// There is no approval field here, just as there is none on the agent's StrategistOutput.
/// Whether this needs a manager is ApprovalBasisDto, computed in C# from the ORDER.
///
/// Strategy is mapped onto <see cref="WorkOrderStrategy"/> — the agent's six values are the
/// API's six in snake_case, mapped once in AgentAnalysis — and is null for a value that
/// matches none of them. Urgency stays a string: it is advice, and nothing acts on it.
/// </summary>
public record AgentProposalDto(
    int StepId,
    int WorkflowId,
    DateTime RecordedAt,

    // "Ok" or "SafeFailure", verbatim from the step.
    string? ValidationResult,
    string? ErrorMessage,
    bool OutputReadable,

    WorkOrderStrategy? Strategy,

    // decimal, read from the JSON number's text — never through a double.
    decimal? EstimatedCost,

    // low / medium / high, as the agent wrote it.
    string? Urgency,

    // At most 500 characters. The agent's account of why, for the manager who decides.
    string? Justification,

    // Only ever non-empty for ConsolidatedJob. An id here is a CLAIM until C# looks it up.
    IReadOnlyList<int> ConsolidateWithWorkOrderIds);
