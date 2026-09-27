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
    bool Reopened = false,

    // The manager's note when an order was sent back for revision (WorkOrder.RevisionNote).
    // Its presence routes graph.py straight to the strategist: the fault is diagnosed
    // already, and what was sent back is the plan for the work. Left off the wire when null,
    // so every other run's body is unchanged.
    [property: JsonPropertyName("revision_note"),
              JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RevisionNote = null,

    // The Draft order that note is on. It is still open in its room, so the strategist's own
    // open-orders lookup returns it; this id is how it tells the job being re-planned from a
    // job it could consolidate with.
    [property: JsonPropertyName("revision_work_order_id"),
              JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? RevisionWorkOrderId = null,

    // Set only when the API asks whether a completed repair held (VerificationAgentService).
    // Its presence routes graph.py START -> verify -> END and nothing else; Description is
    // then the original report's. Left off the wire when null, so every report run's body is
    // unchanged.
    [property: JsonPropertyName("verification"),
              JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentVerificationRequest? Verification = null);

/// <summary>
/// Which repair to judge and what the reporter said — the agent's VerificationRequest,
/// `extra="forbid"`. Everything else the agent weighs it looks up through its own tools, so
/// the API cannot hand it a different account of the repair than the one on the record.
///
/// ReporterConfirmed is null for a check nobody answered: the sweep queues silent checks too,
/// and silence is not a yes. It is written as JSON null, which the agent's `bool | None`
/// accepts. ReporterComment is null or 1–300 characters, the agent's bound and
/// ReporterConfirmationDto's.
/// </summary>
public record AgentVerificationRequest(
    [property: JsonPropertyName("work_order_id")] int WorkOrderId,
    [property: JsonPropertyName("reporter_confirmed")] bool? ReporterConfirmed,
    [property: JsonPropertyName("reporter_comment")] string? ReporterComment);

/// <summary>
/// One answered question, in the shape of the agent's ClarificationAnswer: the question with
/// its answer, so the diagnostic knows what was asked. Both capped on both sides already —
/// QuestionText at 300 by the column, AnswerText at 100 by ClarificationService.
/// </summary>
public record AgentClarificationAnswer(
    [property: JsonPropertyName("question_text")] string QuestionText,
    [property: JsonPropertyName("answer_text")] string AnswerText);
