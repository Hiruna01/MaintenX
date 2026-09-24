using System.Text.Json;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

/// <summary>
/// Reads the diagnostic's and the strategist's STORED output back out of their AgentSteps
/// into typed DTOs, for a human deciding what to do with it.
///
/// THE ONLY PLACE THE API READS THOSE TWO SHAPES, the counterpart of
/// AgentRunResponse.ParseQuestions for the clarifier: the agent's snake_case fields and its
/// strategy values are mapped here once, rather than wherever a reader needs them.
///
/// Read-only and defensive. The payload is what an agent produced, recorded verbatim, so a
/// field that is missing or the wrong type is left null rather than thrown on — the step
/// itself is never touched, and OutputReadable says when this reading came up short. Nothing
/// here judges the diagnosis; it only changes its shape.
/// </summary>
public static class AgentAnalysis
{
    /// <summary>
    /// Whether a step is an AGENT RUN rather than a tool call. Tool calls the diagnostic
    /// makes are recorded under the same AgentName ("diagnostic"), so the name alone is not
    /// enough: an agent-run row carries the empty array in ToolCallsJson, a tool-call row
    /// names its tool there. The web client's agentSteps.js tells them apart the same way.
    /// </summary>
    public static bool IsAgentRunStep(AgentStep step) => IsAgentRunStep(step.ToolCallsJson);

    /// <summary>
    /// The same test on the column alone, for a query that projects ToolCallsJson rather
    /// than loading whole steps (AnalyticsService).
    /// </summary>
    public static bool IsAgentRunStep(string? toolCallsJson)
    {
        if (string.IsNullOrWhiteSpace(toolCallsJson))
        {
            return true;
        }

        try
        {
            using var calls = JsonDocument.Parse(toolCallsJson);
            return calls.RootElement.ValueKind == JsonValueKind.Array
                   && calls.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static AgentDiagnosisDto ToDiagnosis(AgentStep step)
    {
        var hypotheses = new List<DiagnosisHypothesisDto>();
        int? primaryIndex = null;
        string? nextAction = null;
        string? summary = null;

        using (var document = ParseObject(step.PayloadJson))
        {
            if (document is not null)
            {
                var root = document.RootElement;

                if (root.TryGetProperty("hypotheses", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        var cause = String(item, "cause");
                        var confidence = String(item, "confidence");

                        if (cause is null || confidence is null)
                        {
                            continue;
                        }

                        var evidence = new List<string>();

                        if (item.TryGetProperty("evidence", out var evidenceList)
                            && evidenceList.ValueKind == JsonValueKind.Array)
                        {
                            evidence.AddRange(evidenceList.EnumerateArray()
                                .Where(e => e.ValueKind == JsonValueKind.String)
                                .Select(e => e.GetString()!));
                        }

                        hypotheses.Add(new DiagnosisHypothesisDto(cause, confidence, evidence));
                    }
                }

                // An index past the end would point a reader at a cause that is not there.
                if (root.TryGetProperty("primary_hypothesis_index", out var index)
                    && index.TryGetInt32(out var value)
                    && value >= 0 && value < hypotheses.Count)
                {
                    primaryIndex = value;
                }

                nextAction = String(root, "recommended_next_action");
                summary = String(root, "reasoning_summary");
            }
        }

        var readable = hypotheses.Count > 0 && primaryIndex is not null
                       && nextAction is not null && summary is not null;

        return new AgentDiagnosisDto(
            step.Id, step.WorkflowId, step.CreatedAt, step.ValidationResult, step.ErrorMessage,
            readable, hypotheses, primaryIndex, nextAction, summary);
    }

    public static AgentProposalDto ToProposal(AgentStep step)
    {
        WorkOrderStrategy? strategy = null;
        decimal? cost = null;
        string? urgency = null;
        string? justification = null;
        var consolidateWith = new List<int>();

        using (var document = ParseObject(step.PayloadJson))
        {
            if (document is not null)
            {
                var root = document.RootElement;

                strategy = ToStrategy(String(root, "strategy"));

                // GetDecimal reads the number's own text, so 4999.99 arrives as exactly
                // 4999.99 — money never passes through a double on the way in.
                if (root.TryGetProperty("estimated_cost", out var number)
                    && number.ValueKind == JsonValueKind.Number
                    && number.TryGetDecimal(out var parsed))
                {
                    cost = parsed;
                }

                urgency = String(root, "urgency");
                justification = String(root, "justification");

                if (root.TryGetProperty("consolidate_with_work_order_ids", out var ids)
                    && ids.ValueKind == JsonValueKind.Array)
                {
                    foreach (var id in ids.EnumerateArray())
                    {
                        if (id.TryGetInt32(out var orderId))
                        {
                            consolidateWith.Add(orderId);
                        }
                    }
                }
            }
        }

        var readable = strategy is not null && cost is not null
                       && urgency is not null && justification is not null;

        return new AgentProposalDto(
            step.Id, step.WorkflowId, step.CreatedAt, step.ValidationResult, step.ErrorMessage,
            readable, strategy, cost, urgency, justification, consolidateWith);
    }

    /// <summary>
    /// The agent's snake_case strategy onto the C# enum. Null for anything unrecognised —
    /// never a default, because a guessed strategy would put a plan in front of a manager
    /// that nobody proposed.
    /// </summary>
    private static WorkOrderStrategy? ToStrategy(string? value) => value switch
    {
        "known_fix" => WorkOrderStrategy.KnownFix,
        "single_job" => WorkOrderStrategy.SingleJob,
        "consolidated_job" => WorkOrderStrategy.ConsolidatedJob,
        "inspect_first" => WorkOrderStrategy.InspectFirst,
        "defer" => WorkOrderStrategy.Defer,
        "escalate_replacement" => WorkOrderStrategy.EscalateReplacement,
        _ => null
    };

    /// <summary>The payload as a JSON object, or null when there is none or it is not one.</summary>
    private static JsonDocument? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
