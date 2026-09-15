namespace CampusFacilities.Api.Models;

/// <summary>
/// How one visit in an asset's service history ended. Persisted as a string, same rule as
/// every other enum here.
///
/// The distinction between Resolved and TemporaryFix is the point of this enum rather than
/// a nicety: a run of TemporaryFix rows on one asset is exactly the repeat-failure pattern
/// the diagnostic agent is meant to notice.
/// </summary>
public enum ServiceOutcome
{
    Resolved,
    TemporaryFix,
    PartReplaced,
    NoFaultFound
}
