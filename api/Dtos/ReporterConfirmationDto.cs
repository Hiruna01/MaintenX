using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — the reporter's answer to one verification check: "Is the problem fixed?"
///
/// No Id and no reporter id: which check is being answered comes from the route, and who
/// answered comes from the caller's JWT `sub` claim. Same rule as CreateReportDto's missing
/// ReporterId — a client must not be able to answer as somebody else.
///
/// A YES/NO AND A CAPPED COMMENT, AND THAT IS THE WHOLE INPUT. This is the last place in
/// the system a reporter is asked anything, and it is deliberately the narrowest: a toggle
/// and an optional note, submitted once, with no reply coming back. A free-text field with
/// a response would be a chat interface, which this project does not have — the same
/// constraint AnswerType holds on the clarification side.
/// </summary>
public record ReporterConfirmationDto(
    // True = the fault is gone. False = it is still there, which reopens it.
    //
    // NULLABLE WITH [Required], not a plain bool: a plain one binds a missing field as
    // false, and a body that forgot the answer would be recorded as "still broken" —
    // reopening a repair nobody said had failed. Same reason as CreateWorkOrderDto.Strategy.
    [Required] bool? Confirmed,

    // 300, below the column's 500, on purpose: the column is a storage limit, this is the
    // bound on what a reporter may type into a form. Enforced here, by the API, so it is
    // not one curl away from a message box.
    [StringLength(300)] string? Comment);
