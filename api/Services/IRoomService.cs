using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IRoomService
{
    Task<IEnumerable<RoomDto>> GetAllAsync(int? buildingId = null, CancellationToken cancellationToken = default);

    Task<RoomDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the referenced building does not exist (a 400 for the caller).</summary>
    Task<RoomDto?> CreateAsync(CreateRoomDto dto, CancellationToken cancellationToken = default);

    /// <summary>Success, NotFound, or BuildingNotFound when the room is being moved to a building that does not exist.</summary>
    Task<EstateWriteOutcome> UpdateAsync(int id, CreateRoomDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Success, NotFound, or InUse when an asset, a report or a timetabled class still names
    /// the room. Those foreign keys are Restrict on purpose — the history outlives the room —
    /// so the refusal is said as a 409 here rather than thrown out of the driver as a 500.
    /// </summary>
    Task<EstateWriteOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
