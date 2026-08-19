namespace CampusFacilities.Api.Models;

/// <summary>
/// Who a user is allowed to be in the system. Persisted as a string in PostgreSQL
/// (see AppDbContext) so the database reads "FacilitiesManager", not "2".
/// </summary>
public enum Role
{
    Reporter,
    Technician,
    FacilitiesManager,
    Admin
}
