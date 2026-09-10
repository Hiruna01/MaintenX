using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO. No Id — the server assigns it.
///
/// AND NO ReporterId, deliberately. The reporter is read from the caller's JWT `sub`
/// claim in the controller, so there is no field here for a client to put somebody
/// else's user id in. Adding one would let anyone file a report as anyone.
///
/// The 10-character floor mirrors the Flutter client's own validate(), so the two do not
/// drift: the client refuses a one-word description, and so does the server, because a
/// client-side rule is a convenience and never a control.
/// </summary>
public record CreateReportDto(
    [Required]
    [StringLength(1000, MinimumLength = 10)]
    string Description,

    [Range(1, int.MaxValue)] int RoomId);
