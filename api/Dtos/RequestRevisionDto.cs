using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — a manager sending a work order back to be re-planned rather than refusing it.
///
/// THE NOTE IS REQUIRED. It is the only new information the Strategist gets on its second
/// run: a revision request that said nothing would re-run the same planning over the same
/// facts and come back with the same order. It is stored on WorkOrder.RevisionNote, where the
/// runner can read it, because a background run has no request body to read it from.
///
/// Distinct from <see cref="RejectWorkOrderDto"/> on purpose. A rejection ends the work; a
/// revision says "not like this" and keeps it alive. One DTO with a flag would make the
/// difference a boolean a client can get wrong.
/// </summary>
public record RequestRevisionDto(
    [Required]
    [MaxLength(1000)]
    string Note);
