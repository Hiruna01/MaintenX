namespace CampusFacilities.Api.Dtos;

/// <summary>
/// What an allow-listed tool call returns to the agent service.
///
/// <paramref name="Found"/> exists so "no room with that id" is distinguishable from
/// "no such tool": the first is a 200 with Found=false (the tool ran and the answer is
/// nothing), the second is a 404 (the tool does not exist and never will for this caller).
/// Collapsing both into 404 would make an allow-list rejection invisible in the logs.
///
/// For the tools that return a LIST, Found=false means the asset itself was not found,
/// while Found=true with an empty list means it exists and has no history — or nothing
/// open against it. Those are different facts and the services keep them apart
/// deliberately; see IAssetService.GetRecentServiceHistoryAsync.
///
/// Result is whatever response DTO the underlying service returns — RoomDto, BuildingDto,
/// AssetContextDto, a list of ServiceRecordDto or ReportDto — never an entity, and never
/// a judgement the API invented.
/// </summary>
public record ToolCallResponse(string Tool, bool Found, object? Result);
