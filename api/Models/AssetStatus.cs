namespace CampusFacilities.Api.Models;

/// <summary>
/// Where an <see cref="Asset"/> sits in its service life. Persisted as a string in
/// PostgreSQL (see AppDbContext), same as <see cref="Role"/> and
/// <see cref="WorkflowState"/>, so a row reads "UnderMaintenance" rather than "1".
///
/// Retired is why assets are not deleted: an asset carries a service history the
/// diagnostic agent reads, so it is taken out of service, never removed.
/// </summary>
public enum AssetStatus
{
    Active,
    UnderMaintenance,
    Retired
}
