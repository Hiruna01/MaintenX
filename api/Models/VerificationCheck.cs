using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// The question "did that repair actually hold?", asked of the person who reported the
/// fault, a configured number of days after the work order was completed.
///
/// It exists because a completed work order is the technician's account of the work, and
/// nothing in the system before this component ever checked it against the room. The delay
/// is the whole point: asked the same afternoon, every answer is yes, because an
/// intermittent fault has not had time to come back. See VerificationSettings.DelayDays.
///
/// One check per completed work order. A reopened fault produces a NEW work order with its
/// own check rather than reusing this row — this one is the record of an answer that was
/// given, and re-asking the same row would overwrite the evidence that the first repair
/// failed. That is why the index on <see cref="WorkOrderId"/> is not unique.
/// </summary>
public class VerificationCheck
{
    public int Id { get; set; }

    /// <summary>The completed work order whose repair is being verified.</summary>
    public int WorkOrderId { get; set; }

    public WorkOrder? WorkOrder { get; set; }

    /// <summary>
    /// The equipment the repair was carried out on.
    ///
    /// Copied from the work order rather than read through it, and that is deliberate. The
    /// question this table is built to answer is "how often does work on this machine
    /// actually hold?" — a reopen rate per asset — and making that a join through
    /// WorkOrders would put the most useful query in the system behind the table it is
    /// least interested in. It is the same reasoning that keeps AssetId on Report.
    /// </summary>
    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    /// <summary>
    /// When this check falls due: the work order's completion plus
    /// VerificationSettings.DelayDays. UTC, and a DateTime rather than the DateOnly the
    /// asset registry uses, because the sweep compares it against "now" on an interval of
    /// minutes and a date would make every check in a day fall due at once.
    /// </summary>
    public DateTime DueAt { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;

    /// <summary>
    /// The reporter's verdict. NULL UNTIL THEY ANSWER, and that is why it is a bool? rather
    /// than a bool defaulting to false: "not answered yet" and "answered no" are completely
    /// different facts, and a non-nullable bool would quietly record every unanswered check
    /// as a failed repair. <see cref="Status"/> says which of the two it is.
    /// </summary>
    public bool? ReporterConfirmed { get; set; }

    /// <summary>
    /// What the reporter added, if anything. Short and optional — this is a confirmation
    /// step, not a conversation, and there is no reply to whatever is written here. The
    /// cap matches ClarificationAnswer's reasoning: bounded input, never a message box.
    /// </summary>
    [MaxLength(500)]
    public string? ReporterComment { get; set; }

    public DateTime? ReporterRespondedAt { get; set; }

    /// <summary>
    /// What the verification agent concluded, as its own short label. A string rather than
    /// an enum precisely because it is the MODEL'S opinion: giving it a C# enum would imply
    /// the system acts on it, and the system does not. <see cref="Status"/> is set by C#
    /// from the reporter's answer; this column is recorded alongside it and read by humans.
    /// </summary>
    [MaxLength(100)]
    public string? AgentOutcome { get; set; }

    /// <summary>The agent's reasoning for <see cref="AgentOutcome"/>, stored verbatim.</summary>
    [MaxLength(2000)]
    public string? AgentReason { get; set; }

    /// <summary>
    /// When the sweep last acted on this row. Distinct from <see cref="UpdatedAt"/>, which
    /// AppDbContext stamps on every write: this one says the SWEEP touched it, so a check
    /// that is sitting still can be told apart from one the sweep keeps picking up and
    /// failing to move on.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
