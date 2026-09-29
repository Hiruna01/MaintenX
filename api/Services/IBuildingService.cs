using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IBuildingService
{
    Task<IEnumerable<BuildingDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<BuildingDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns null when another building already has that code (a 409 for the caller).</summary>
    Task<BuildingDto?> CreateAsync(CreateBuildingDto dto, CancellationToken cancellationToken = default);

    /// <summary>Success, NotFound, or CodeTaken when another building already has the new code.</summary>
    Task<EstateWriteOutcome> UpdateAsync(int id, CreateBuildingDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Success, NotFound, or InUse when the building still has rooms. A building's rooms would
    /// otherwise go with it (the foreign key cascades), taking every asset, report and class
    /// in them to a constraint failure — so a building is only removed once it is empty.
    /// </summary>
    Task<EstateWriteOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a write to a building or a room did, so the controller can pick the status code
/// instead of a constraint violation surfacing as a 500.
/// </summary>
public enum EstateWriteOutcome
{
    /// <summary>Written. 204.</summary>
    Success,

    /// <summary>No such building or room. 404.</summary>
    NotFound,

    /// <summary>Another building already has that code. 409.</summary>
    CodeTaken,

    /// <summary>Something still refers to it, so it cannot be deleted. 409.</summary>
    InUse,

    /// <summary>A room was pointed at a building that does not exist. 400.</summary>
    BuildingNotFound
}
