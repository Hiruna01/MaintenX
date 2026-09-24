using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — what the technician found and what it cost, submitted when the work is done.
///
/// This is the request that ENDS the order's life as live work: completing it appends a
/// ServiceRecord against the asset, and that record is immutable history the diagnostic
/// agent reads. So the note is required rather than optional — a completed job with no
/// account of what was done is a gap in the very history the agent exists to read across.
///
/// No CompletedAt: when it finished is stamped by the server, not claimed by the client.
/// </summary>
public record CompleteWorkOrderDto(
    // Same decimal bounding as CreateWorkOrderDto.EstimatedCost, and never through a
    // double. This is the figure any overspend rule compares against the estimate — and
    // nullable with [Required] for the same reason too: a missing cost must be a 400, not a
    // job recorded as free.
    [Required]
    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true)]
    decimal? ActualCost,

    // Copied into ServiceRecord.Outcome. Required and nullable so a missing value is a 400
    // rather than the enum's first member: silently recording a TemporaryFix as Resolved
    // would erase exactly the repeat-failure pattern the diagnostic agent reads for.
    [Required] ServiceOutcome? Outcome,

    // Copied verbatim into ServiceRecord.TechnicianNote, which the diagnostic agent reads
    // across months of history looking for a repeat failure. The floor is there to refuse
    // "done" — a note that says nothing is worse than a missing one, because it looks like
    // evidence. Twenty characters is roughly "what was wrong, what was done": the shortest
    // note a diagnostic run months later can still learn something from. Both clients hold
    // the same floor, but this is the one that counts.
    [Required]
    [StringLength(2000, MinimumLength = 20)]
    string ResolutionNote,

    // A URL to wherever the image is stored, never the image bytes — same rule as
    // Report.PhotoUrl. Optional: most jobs are closed without a photo. Left out, the order
    // keeps the photo already uploaded through POST {id}/photo; sent, it replaces it.
    [MaxLength(500)] string? CompletionPhotoUrl);
