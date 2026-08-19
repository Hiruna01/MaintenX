using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IRoomService
{
    Task<IEnumerable<RoomDto>> GetAllAsync(int? buildingId = null, CancellationToken cancellationToken = default);

    Task<RoomDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the referenced building does not exist (a 400 for the caller).</summary>
    Task<RoomDto?> CreateAsync(CreateRoomDto dto, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the room, or the building it is being moved to, does not exist.</summary>
    Task<bool> UpdateAsync(int id, CreateRoomDto dto, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
