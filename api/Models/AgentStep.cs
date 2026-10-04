using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// One recorded action inside a workflow — an agent turn or a tool call. This is the
/// audit trail: every call the agent service makes back into the API writes one of these,
/// so a marker can see exactly what was asked for and what came back.
/// </summary>
public class AgentStep
{
    public int Id { get; set; }

    public int WorkflowId { get; set; }

    public AgentWorkflow? Workflow { get; set; }

    /// <summary>Which agent (or tool caller) produced this step.</summary>
    [Required]
    [MaxLength(100)]
    public string AgentName { get; set; } = string.Empty;

    /// <summary>The tool calls made in this step. PostgreSQL jsonb, not text.</summary>
    public string? ToolCallsJson { get; set; }

    public int DurationMs { get; set; }

    /// <summary>
    /// How many LLM attempts an agent-level step took: 1 when the first reply validated, 2
    /// when it took the one retry. Null on a tool-call row, which makes no model call, and on
    /// a step whose agent did not report it.
    /// </summary>
    public int? Attempts { get; set; }

    /// <summary>
    /// The tokens the provider REPORTED for an agent-level step's model calls, both attempts
    /// added together (AddAgentStepTokenUsage). Null on a tool-call row and an approval step,
    /// which make no model call, and whenever nothing was reported — STUB_MODE, a provider that
    /// does not send usage, a call that failed, or a step recorded before the columns existed.
    /// Null is not zero: AgentMetricsService counts how many runs reported usage, and costs
    /// only those.
    /// </summary>
    public int? PromptTokens { get; set; }

    /// <inheritdoc cref="PromptTokens"/>
    public int? CompletionTokens { get; set; }

    /// <summary>Short outcome tag, e.g. "Ok", "NotFound", "RejectedUnknownTool".</summary>
    [MaxLength(100)]
    public string? ValidationResult { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>What the step returned. PostgreSQL jsonb, not text.</summary>
    public string? PayloadJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
