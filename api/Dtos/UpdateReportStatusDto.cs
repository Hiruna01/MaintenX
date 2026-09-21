using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — where a facilities manager is moving a report to. No Id: the report is named
/// by the route, and an id in the body could disagree with it.
///
/// One field, because that is the whole operation. This is deliberately NOT a general
/// "update the report" DTO: description, room and asset are the reporter's account of the
/// fault, and a status endpoint that could quietly rewrite them would be an edit wearing a
/// workflow action's name.
///
/// Whether the move is LEGAL is not expressed here and cannot be — a DataAnnotation sees
/// the value being set and not the value being moved from. That check is a deterministic
/// business rule over both, so it lives in C# in ReportService, and an illegal move is a
/// 409 rather than a 400.
/// </summary>
public record UpdateReportStatusDto(
    [Required]
    [EnumDataType(typeof(ReportStatus))]
    ReportStatus Status);
