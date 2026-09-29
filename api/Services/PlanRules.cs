using System.Text.Json;
using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

/// <summary>The four states of one plan step. Strings, because they live inside PlanJson.</summary>
public static class PlanStepStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";

    /// <summary>The agent ran and safe-failed: it produced no usable output.</summary>
    public const string Failed = "failed";

    /// <summary>
    /// The plan named the agent but the run did not reach it. Only ever set when the agent
    /// service and this class disagreed about the plan — see WorkflowRunner.
    /// </summary>
    public const string Skipped = "skipped";
}

/// <summary>
/// THE DETERMINISTIC CHECK ON THE PLANNER'S PLAN, and the one place PlanJson is built and
/// changed.
///
/// The planner agent PROPOSES a plan; this class decides whether the system runs from it.
/// The agent service validates the plan against its own Pydantic schema before it acts on
/// it, and the same rules are checked again here, on this side of the network, before the
/// plan is stored — the same instinct as the tool allow-list living in C# as well as in each
/// agent's ALLOWED_TOOLS. A plan that fails is not stored as the workflow's plan: the
/// fallback is, with the reason, and the planner's reply stays on its own AgentStep.
///
/// The rules are pure functions over JSON, with no database, so PlanRulesTests can pin
/// every one of them without booting the API.
/// </summary>
public static class PlanRules
{
    public const string SourcePlanner = "planner";
    public const string SourceFallback = "fallback";

    public const string AddedByPlanner = "planner";
    public const string AddedByApi = "api";

    /// <summary>The agent-run AgentStep name the planner's reply is recorded under.</summary>
    public const string PlannerAgentName = "planner";

    /// <summary>The same limits as the agent's PlannerOutput (agent/schemas.py).</summary>
    public const int MinSteps = 2;
    public const int MaxSteps = 3;
    public const int MaxPurposeLength = 200;
    public const int MaxRationaleLength = 300;

    /// <summary>
    /// The only agents a report run may be delegated to, in the only order they may run.
    /// A plan may leave the clarifier out; it may not leave anything else out, reorder it,
    /// repeat it, or name an agent that is not here — the verification agent included,
    /// because a verification is a different run about a different question.
    /// </summary>
    public static readonly IReadOnlyList<string> PipelineOrder = new[]
    {
        AgentRunResponse.ClarifierAgentName,
        AgentRunResponse.DiagnosticAgentName,
        AgentRunResponse.StrategistAgentName
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Checks the planner's output and turns it into a plan, or returns why it cannot be used.
    ///
    /// A valid plan has 2 or 3 steps and a rationale; each step names an agent from
    /// <see cref="PipelineOrder"/> and a purpose; the agents appear in pipeline order with no
    /// repeats; and the diagnostic and the strategist are both there. Everything is read
    /// defensively — the caller is a background worker, and a malformed plan is a reason to
    /// fall back, never an exception.
    /// </summary>
    public static (WorkflowPlanDto? Plan, string? Reason) Validate(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object)
        {
            return (null, "The plan is not a JSON object.");
        }

        if (!output.TryGetProperty("rationale", out var rationaleElement)
            || rationaleElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(rationaleElement.GetString()))
        {
            return (null, "The plan has no rationale.");
        }

        var rationale = rationaleElement.GetString()!;

        if (rationale.Length > MaxRationaleLength)
        {
            return (null, $"The rationale is longer than {MaxRationaleLength} characters.");
        }

        if (!output.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
        {
            return (null, "The plan has no list of steps.");
        }

        var count = stepsElement.GetArrayLength();

        if (count is < MinSteps or > MaxSteps)
        {
            return (null, $"The plan has {count} steps; it must have {MinSteps} or {MaxSteps}.");
        }

        var steps = new List<WorkflowPlanStepDto>();
        var lastPosition = -1;

        foreach (var step in stepsElement.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object
                || !step.TryGetProperty("agent", out var agentElement)
                || agentElement.ValueKind != JsonValueKind.String
                || !step.TryGetProperty("purpose", out var purposeElement)
                || purposeElement.ValueKind != JsonValueKind.String)
            {
                return (null, $"Step {steps.Count + 1} is not an agent with a purpose.");
            }

            var agent = agentElement.GetString()!;
            var purpose = purposeElement.GetString()!;

            // Ordinal, like the tool allow-list: "Diagnostic" is not "diagnostic".
            var position = IndexInPipeline(agent);

            if (position < 0)
            {
                return (null, $"Step {steps.Count + 1} names '{agent}', which is not an agent a report run can use.");
            }

            if (position <= lastPosition)
            {
                return (null, $"Step {steps.Count + 1} ('{agent}') is repeated or out of order.");
            }

            if (string.IsNullOrWhiteSpace(purpose) || purpose.Length > MaxPurposeLength)
            {
                return (null, $"Step {steps.Count + 1} needs a purpose of 1 to {MaxPurposeLength} characters.");
            }

            lastPosition = position;
            steps.Add(new WorkflowPlanStepDto(
                steps.Count + 1, agent, purpose.Trim(), PlanStepStatus.Pending, AddedByPlanner));
        }

        foreach (var required in new[] { AgentRunResponse.DiagnosticAgentName, AgentRunResponse.StrategistAgentName })
        {
            if (steps.All(s => s.Agent != required))
            {
                return (null, $"The plan leaves out the {required}, which every report run needs.");
            }
        }

        return (new WorkflowPlanDto(SourcePlanner, rationale.Trim(), Note: null, steps), null);
    }

    /// <summary>
    /// The plan used when the planner's cannot be: every agent, in order. The clarifier is
    /// IN it — when nobody has decided that the report is clear enough, asking is the safe
    /// default, and a clarifier with nothing to ask asks nothing.
    /// </summary>
    public static WorkflowPlanDto Fallback(string note) => new(
        SourceFallback,
        Rationale: null,
        note,
        new[]
        {
            new WorkflowPlanStepDto(1, AgentRunResponse.ClarifierAgentName,
                "Ask the reporter for anything a technician would need that the report does not say.",
                PlanStepStatus.Pending, AddedByApi),
            new WorkflowPlanStepDto(2, AgentRunResponse.DiagnosticAgentName,
                "Propose the likely causes of the fault from the asset's service history.",
                PlanStepStatus.Pending, AddedByApi),
            new WorkflowPlanStepDto(3, AgentRunResponse.StrategistAgentName,
                "Propose how to resolve it: a strategy and an estimated cost for a manager to review.",
                PlanStepStatus.Pending, AddedByApi)
        });

    /// <summary>True when the plan delegates a step to the clarifier.</summary>
    public static bool IncludesClarifier(WorkflowPlanDto plan) =>
        plan.Steps.Any(s => s.Agent == AgentRunResponse.ClarifierAgentName);

    /// <summary>
    /// Sets the FIRST still-pending step for <paramref name="agent"/> to <paramref name="status"/>.
    /// A plan with no pending step for that agent comes back unchanged — the step was already
    /// settled, or the plan never delegated to it.
    /// </summary>
    public static WorkflowPlanDto MarkStep(WorkflowPlanDto plan, string agent, string status)
    {
        var index = plan.Steps.ToList().FindIndex(s => s.Agent == agent && s.Status == PlanStepStatus.Pending);

        if (index < 0)
        {
            return plan;
        }

        var steps = plan.Steps.ToList();
        steps[index] = steps[index] with { Status = status };
        return plan with { Steps = steps };
    }

    /// <summary>
    /// A repair did not hold, so the run goes back to the diagnostic and the strategist. Their
    /// new steps are APPENDED, like their new AgentSteps, so the first diagnosis stays in the
    /// plan beside the second rather than being reset.
    /// </summary>
    public static WorkflowPlanDto AppendRediagnosis(WorkflowPlanDto plan, int reopenedWorkOrderId)
    {
        // Idempotent: a re-diagnosis still pending was appended by an earlier pass that never
        // finished — a restart re-queues the same Diagnosing workflow — so it is not added twice.
        if (plan.Steps.Any(s => s.Agent == AgentRunResponse.DiagnosticAgentName && s.Status == PlanStepStatus.Pending))
        {
            return plan;
        }

        var next = plan.Steps.Count + 1;
        var steps = plan.Steps.ToList();

        steps.Add(new WorkflowPlanStepDto(next, AgentRunResponse.DiagnosticAgentName,
            $"Diagnose again: the repair on work order #{reopenedWorkOrderId} did not hold.",
            PlanStepStatus.Pending, AddedByApi));
        steps.Add(new WorkflowPlanStepDto(next + 1, AgentRunResponse.StrategistAgentName,
            "Propose a new resolution in the light of the failed repair.",
            PlanStepStatus.Pending, AddedByApi));

        return plan with { Steps = steps };
    }

    public static string Serialize(WorkflowPlanDto plan) => JsonSerializer.Serialize(plan, Json);

    /// <summary>
    /// Reads a stored PlanJson back. Null when there is none — a workflow from before plans
    /// existed — or when it cannot be read, which is never thrown on: the raw text is still
    /// on the row.
    /// </summary>
    public static WorkflowPlanDto? Read(string? planJson)
    {
        if (string.IsNullOrWhiteSpace(planJson))
        {
            return null;
        }

        try
        {
            var plan = JsonSerializer.Deserialize<WorkflowPlanDto>(planJson, Json);
            return plan?.Steps is null ? null : plan;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int IndexInPipeline(string agent)
    {
        for (var i = 0; i < PipelineOrder.Count; i++)
        {
            if (string.Equals(PipelineOrder[i], agent, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
