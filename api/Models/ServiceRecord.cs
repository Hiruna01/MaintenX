using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// One completed maintenance visit against an asset — the closed historical record.
///
/// THIS IS DELIBERATELY NOT THE SAME TABLE AS WorkOrder (Component C).
/// A WorkOrder is live work in progress: assigned, scheduled, still changing. A
/// ServiceRecord is what is left once that work is finished and will not change again.
/// Component C appends a ServiceRecord when a work order completes, and fills in
/// <see cref="WorkOrderId"/> to point back at the order that produced it.
///
/// They are separate because they are read for different reasons and by different things.
/// The diagnostic agent reads this table to find repeat failures across months of history;
/// it has no business seeing half-finished work, and history must not shift under it every
/// time a technician updates a live order. Merging them would also mean one table carrying
/// both a scheduling lifecycle and an immutable record, which is two jobs.
/// </summary>
public class ServiceRecord
{
    public int Id { get; set; }

    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    /// <summary>Calendar date of the visit — DateOnly, same reasoning as Asset.InstalledOn.</summary>
    public DateOnly ServicedOn { get; set; }

    [Required]
    [MaxLength(200)]
    public string TechnicianName { get; set; } = string.Empty;

    /// <summary>
    /// What the technician wrote, in their own words — terse, abbreviated and sometimes
    /// vague, because that is how maintenance notes are actually written. It is stored
    /// verbatim and never normalised: the wording is the evidence, and the diagnostic
    /// agent's whole job is reading across several of these to spot a pattern a single
    /// note does not show.
    /// </summary>
    [MaxLength(2000)]
    public string? TechnicianNote { get; set; }

    public ServiceOutcome Outcome { get; set; }

    /// <summary>
    /// The work order this record came from, once Component C exists. Nullable and
    /// deliberately NOT a foreign key yet — there is no WorkOrder table to point at, and a
    /// constraint cannot be written against a table that does not exist. It becomes a real
    /// foreign key when that table lands, the same way AgentWorkflow.ReportId did.
    ///
    /// Null also stays legitimate afterwards: these seeded rows, and any history imported
    /// from before the system existed, were never produced by a work order.
    /// </summary>
    public int? WorkOrderId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
