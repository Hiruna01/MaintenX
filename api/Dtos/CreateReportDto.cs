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
///
/// AssetId is a default-null trailing parameter so every existing caller that files a report
/// without one still compiles and still means the same thing.
/// </summary>
public record CreateReportDto(
    [Required]
    [StringLength(1000, MinimumLength = 10)]
    string Description,

    [Range(1, int.MaxValue)] int RoomId,

    // Optional, and null in the normal case: a reporter is not expected to know which asset
    // tag the projector carries. Set when they scanned its sticker on the report form, so
    // the agents can read that machine's service history from the first run. It must be an
    // asset registered in RoomId — a sticker says where the machine is, and a report naming
    // a machine in some other room would be two contradicting accounts of the same fault.
    [Range(1, int.MaxValue)] int? AssetId = null);
