namespace CampusFacilities.Api.Dtos;

/// <summary>
/// What an allow-listed tool call returns to the agent service.
///
/// <paramref name="Found"/> exists so "no room with that id" is distinguishable from
/// "no such tool": the first is a 200 with Found=false (the tool ran and the answer is
/// nothing), the second is a 404 (the tool does not exist and never will for this caller).
/// Collapsing both into 404 would make an allow-list rejection invisible in the logs.
///
/// Result is whatever response DTO the underlying service returns — RoomDto,
/// BuildingDto — never an entity.
/// </summary>
public record ToolCallResponse(string Tool, bool Found, object? Result);
