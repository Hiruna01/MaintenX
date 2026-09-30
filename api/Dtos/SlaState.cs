namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Where a work order stands against the repair SLA, decided by SlaRules.StateOf so no
/// client compares DueAt with a clock of its own. Sent by NAME, like every enum here.
/// </summary>
public enum SlaState
{
    /// <summary>
    /// No clock is running: not approved yet, rejected or cancelled, or an order from before
    /// the SLA existed (DueAt null). Not "on track" — nothing was promised.
    /// </summary>
    None,

    /// <summary>Approved, not finished, and the due time has not passed.</summary>
    OnTrack,

    /// <summary>Approved, not finished, and the due time has passed.</summary>
    Overdue,

    /// <summary>Completed at or before the due time.</summary>
    Met,

    /// <summary>Completed after the due time.</summary>
    Missed
}
