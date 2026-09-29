using System.Text.Json;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The workflow's audit copy of the approval gate and of the manager's decision on it.
///
/// The work order already holds the decision (Status, ApprovedBy, ApprovedAt, the reason or
/// the note), but that is the ORDER's record, and it is overwritten: a revised order goes
/// back to Draft and loses the fact that it was ever sent back. The workflow's AgentSteps are
/// the run's audit trail, so the human pause is written there too — one step when an order is
/// raised (where the gate routed it) and one per decision — in the SAME SaveChanges as the
/// move it records, so the trail can never show a decision the workflow did not make.
///
/// These rows are neither an agent run nor a tool call, and they say so: AgentName
/// <see cref="StepName"/>, ToolCallsJson null, no duration, no attempts. Every C# reader of
/// agent runs picks steps by the agent's name first, so an "approval" row is never read as
/// a diagnosis or a proposal. The web client's agentSteps.js renders them as their own kind.
/// </summary>
public static class ApprovalAudit
{
    public const string StepName = "approval";

    // The ValidationResult tags. Deliberately not "Approved" / "Rejected": "Rejected" is
    // already the planner's refused plan, and a manager saying no is not a system failure.
    public const string ApprovalRequired = "ApprovalRequired";
    public const string AutoApproved = "AutoApproved";
    public const string ManagerApproved = "ManagerApproved";
    public const string ManagerRejected = "ManagerRejected";
    public const string RevisionRequested = "RevisionRequested";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The step for one approval event. <paramref name="basis"/> is the gate's reading AT THIS
    /// MOMENT — unlike ApprovalBasisDto on the order, which reads the threshold configured
    /// now, this copy keeps the threshold the decision was actually made against.
    /// <paramref name="decidedByUserId"/> is null when the API's threshold routed the order
    /// and nobody decided.
    /// </summary>
    public static AgentStep Step(
        AgentWorkflow workflow,
        WorkOrder order,
        string decision,
        ApprovalBasisDto basis,
        int? decidedByUserId,
        string? reason = null,
        string? note = null) =>
        new()
        {
            WorkflowId = workflow.Id,
            AgentName = StepName,
            ToolCallsJson = null,
            DurationMs = 0,
            ValidationResult = decision,
            PayloadJson = JsonSerializer.Serialize(
                new ApprovalStepPayload(
                    order.Id, decision, order.Strategy.ToString(), order.EstimatedCost,
                    basis, decidedByUserId, reason, note),
                Json)
        };
}

/// <summary>
/// What an approval step stores in PayloadJson (camelCase). Ids, not names: the audit trail
/// is also shown on the report's own page, and a user id says who decided without putting a
/// name in front of the reporter.
/// </summary>
public record ApprovalStepPayload(
    int WorkOrderId,
    string Decision,
    string Strategy,
    decimal EstimatedCost,
    ApprovalBasisDto Basis,
    int? DecidedByUserId,
    string? Reason,
    string? Note);
