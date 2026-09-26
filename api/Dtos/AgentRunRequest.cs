using System.Text.Json.Serialization;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// The body of POST /run on the Python agent service.
///
/// EVERY PROPERTY NAME IS SPELLED OUT. The agent's RunRequest is a Pydantic model with
/// `extra="forbid"` and snake_case fields, so this API's default camelCase serialisation
/// would be rejected wholesale with a 422 — "workflowId" is not "workflow_id", and an
/// unrecognised field is an error rather than something ignored. [JsonPropertyName] is
/// used rather than a naming policy so the mapping is visible at the point it matters and
/// cannot be broken by a change to the global serializer options.
/// </summary>
public record AgentRunRequest(
    [property: JsonPropertyName("workflow_id")] int WorkflowId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("room_id")] int? RoomId,
    [property: JsonPropertyName("building_id")] int? BuildingId,

    // The equipment at fault, when triage or a QR scan has named it — null otherwise, which
    // is the normal case for a fresh report (see Report.AssetId). It is what lets the
    // diagnostic and the strategist read the asset's service history through their tools;
    // without it they reason from the report text alone.
    [property: JsonPropertyName("asset_id")] int? AssetId = null,

    // The reporter's answers to the questions THIS workflow's clarifier asked, sent when the
    // run resumes after human pause 1. Their presence is what routes graph.py straight to
    // the diagnostic, so the clarifier is not asked again about a report it already asked
    // about. Omitted from the body when null — the agent's field is a list, and a JSON null
    // there would be a 422.
    [property: JsonPropertyName("clarification_answers"),
              JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AgentClarificationAnswer>? ClarificationAnswers = null,

    // True when verification reopened this workflow: the repair did not hold and the fault is
    // being diagnosed again. Like the answers, it routes graph.py straight to the diagnostic —
    // re-clarifying a fault somebody already repaired would put the same questions to the
    // reporter again. It carries no data of its own: what changed since the first diagnosis
    // (the repair's ServiceRecord, the reports filed since) the diagnostic reads through its
    // tools. Left off the wire when false, so every other run's body is unchanged.
    [property: JsonPropertyName("reopened"),
              JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Reopened = false);

/// <summary>
/// One answered question, in the shape of the agent's ClarificationAnswer: the question with
/// its answer, so the diagnostic knows what was asked. Both capped on both sides already —
/// QuestionText at 300 by the column, AnswerText at 100 by ClarificationService.
/// </summary>
public record AgentClarificationAnswer(
    [property: JsonPropertyName("question_text")] string QuestionText,
    [property: JsonPropertyName("answer_text")] string AnswerText);
