namespace CampusFacilities.Api.Dtos;

/// <summary>
/// A workflow's structured plan: which agents the run is delegated to, in what order, why,
/// and how far each has got. Stored as <c>AgentWorkflow.PlanJson</c> (jsonb) in exactly this
/// shape, camelCase, and returned on <c>WorkflowDetailDto.Plan</c>.
///
/// Written once when a run starts — from the planner agent's plan after PlanRules has
/// re-checked it, or from PlanRules' fallback when there was no valid plan to use — and then
/// only its step statuses change as the runner records each agent. The planner's own reply is
/// also kept verbatim on its AgentStep, so what the model said and what the system ran from
/// can always be told apart.
/// </summary>
public record WorkflowPlanDto(
    // "planner" when the steps are the planner agent's, re-checked in C#; "fallback" when
    // they are PlanRules' default because the planner failed, was rejected, or never ran.
    string Source,

    // The planner's one-line reason for the plan. Null on a fallback plan.
    string? Rationale,

    // Why a fallback plan was used. Null on a planner plan.
    string? Note,

    IReadOnlyList<WorkflowPlanStepDto> Steps);

/// <summary>One delegated step of a <see cref="WorkflowPlanDto"/>.</summary>
public record WorkflowPlanStepDto(
    // 1, 2, 3 … in the order the steps run.
    int Order,

    // The agent the step is delegated to — the same name its AgentStep is recorded under
    // ("clarifier", "diagnostic", "strategist").
    string Agent,

    // What this agent is to establish for THIS report, in the planner's words — or the
    // API's, on a fallback plan or a step it appended.
    string Purpose,

    // pending, completed, failed or skipped — see PlanStepStatus.
    string Status,

    // "planner" for a step the planner proposed, "api" for one the API wrote: a fallback
    // plan's steps, or the re-diagnosis appended when a repair did not hold.
    string AddedBy);
