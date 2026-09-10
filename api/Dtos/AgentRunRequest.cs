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
    [property: JsonPropertyName("building_id")] int? BuildingId);
