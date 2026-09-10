using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// The agent service's reply to POST /run. snake_case for the same reason as
/// AgentRunRequest.
///
/// A reply with status "safe_failure" is still a 200 with a well-formed body — that is the
/// agent's contract, not an error condition on the wire — so the runner has to read this
/// field rather than relying on the status code.
/// </summary>
public record AgentRunResponse(
    [property: JsonPropertyName("workflow_id")] int WorkflowId,
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("status")] string Status,

    // Kept as raw JSON rather than mapped to C# types: it is written straight into the
    // AgentStep.PayloadJson jsonb column, and re-modelling the clarifier's question shape
    // here would be a second copy of a schema that already lives in agent/schemas.py.
    [property: JsonPropertyName("output")] JsonElement Output,

    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("tool_calls")] JsonElement ToolCalls)
{
    /// <summary>The status string the agent sends when it could not produce real output.</summary>
    public const string SafeFailureStatus = "safe_failure";

    public bool IsSafeFailure => string.Equals(Status, SafeFailureStatus, StringComparison.Ordinal);

    /// <summary>
    /// How many questions the clarifier asked. Read defensively: this is the one place the
    /// API reaches into the agent's payload shape, and a missing or oddly-shaped `output`
    /// must not throw inside a background worker.
    /// </summary>
    public int QuestionCount =>
        Output.ValueKind == JsonValueKind.Object
        && Output.TryGetProperty("questions", out var questions)
        && questions.ValueKind == JsonValueKind.Array
            ? questions.GetArrayLength()
            : 0;
}
