using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// A maintenance fault reported by a user against a room. This is the entity the whole
/// vertical slice starts from: the mobile client posts one, the API persists it and
/// raises an <see cref="AgentWorkflow"/> for it in the background.
/// </summary>
public class Report
{
    public int Id { get; set; }

    /// <summary>
    /// Who filed it. Taken from the caller's JWT `sub` claim, never from the request
    /// body — CreateReportDto has no such field, so a client cannot file a report as
    /// somebody else.
    /// </summary>
    public int ReporterId { get; set; }

    public User? Reporter { get; set; }

    public int RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>
    /// What is wrong, in the reporter's own words.
    ///
    /// Capped at the same 1000 characters as <see cref="AgentWorkflow.Objective"/> on
    /// purpose: the description becomes that workflow's objective verbatim, so a longer
    /// description would create a report that can never have a workflow raised for it.
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public ReportStatus Status { get; set; } = ReportStatus.Submitted;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
