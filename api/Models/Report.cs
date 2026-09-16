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

    /// <summary>
    /// The specific piece of equipment at fault, when it is known.
    ///
    /// NULLABLE ON PURPOSE, and it is not a field the reporter is nagged for: someone
    /// reporting that "the projector keeps cutting out" is not expected to know which
    /// asset tag that projector carries, and a form that demanded one would either be
    /// abandoned or answered with a guess. It is filled in later — by a QR scan at the
    /// point of reporting, or by whoever triages the report.
    /// </summary>
    public int? AssetId { get; set; }

    public Asset? Asset { get; set; }

    /// <summary>
    /// Where the fault has got to. Distinct from the state of any workflow raised for it;
    /// see <see cref="ReportStatus"/>.
    /// </summary>
    public ReportStatus Status { get; set; } = ReportStatus.Submitted;

    /// <summary>
    /// A photo of the fault, as a URL to wherever the image is stored — never the image
    /// bytes, which have no business in a row that is read on every list query.
    ///
    /// Null until the mobile client's photo attachment lands (it is the disabled
    /// TODO(photo) button on SubmitReportScreen today), and legitimately null forever
    /// after that: most faults are reported without one.
    /// </summary>
    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The questions the clarifier asked about this report, across every run. Ordered by
    /// DisplayOrder when read, not by the order the database happens to return them in.
    /// </summary>
    public ICollection<ClarificationQuestion> ClarificationQuestions { get; set; } =
        new List<ClarificationQuestion>();
}
