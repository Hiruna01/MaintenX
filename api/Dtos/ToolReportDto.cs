using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One open report as `get_related_open_reports` hands it to an agent: what was reported,
/// where, where it has got to and when — and not who reported it or where their photo is.
///
/// Not <see cref="ReportDto"/>: that carries the reporter's id and the photo's public URL,
/// neither of which an agent reads, and both of which every tool call would otherwise copy
/// into AgentStep.PayloadJson and send on to the LLM provider.
/// </summary>
public record ToolReportDto(
    int Id,
    int RoomId,
    int? AssetId,
    string Description,
    ReportStatus Status,
    DateTime CreatedAt);
