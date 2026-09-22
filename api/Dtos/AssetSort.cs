namespace CampusFacilities.Api.Dtos;

/// <summary>
/// How GET /api/assets orders its results. A query contract, not a persisted value, so it
/// lives here beside the DTOs rather than in Models.
///
/// An enum rather than a free string because the value reaches an ORDER BY: a string would
/// have to be validated against a list somewhere anyway, and model binding already returns
/// 400 for a member that does not exist. Bound by NAME, like every other enum in this API.
/// </summary>
public enum AssetSort
{
    /// <summary>Alphabetical by name — the default, and how a person scans a list.</summary>
    Name,

    /// <summary>Oldest installation first, which is the order equipment comes up for replacement.</summary>
    InstalledOn
}
