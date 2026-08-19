using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class RoomService : IRoomService
{
    private readonly AppDbContext _db;

    public RoomService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<RoomDto>> GetAllAsync(int? buildingId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Rooms.AsNoTracking();

        if (buildingId is not null)
        {
            query = query.Where(r => r.BuildingId == buildingId);
        }

        return await query
            .OrderBy(r => r.Code)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken);
    }

    public async Task<RoomDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var room = await _db.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return room is null ? null : ToDto(room);
    }

    public async Task<RoomDto?> CreateAsync(CreateRoomDto dto, CancellationToken cancellationToken = default)
    {
        var buildingExists = await _db.Buildings.AnyAsync(b => b.Id == dto.BuildingId, cancellationToken);
        if (!buildingExists)
        {
            return null;
        }

        var room = new Room
        {
            BuildingId = dto.BuildingId,
            Name = dto.Name,
            Code = dto.Code,
            Floor = dto.Floor
        };

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(room);
    }

    public async Task<bool> UpdateAsync(int id, CreateRoomDto dto, CancellationToken cancellationToken = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room is null)
        {
            return false;
        }

        var buildingExists = await _db.Buildings.AnyAsync(b => b.Id == dto.BuildingId, cancellationToken);
        if (!buildingExists)
        {
            return false;
        }

        room.BuildingId = dto.BuildingId;
        room.Name = dto.Name;
        room.Code = dto.Code;
        room.Floor = dto.Floor;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room is null)
        {
            return false;
        }

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static RoomDto ToDto(Room r) =>
        new(r.Id, r.BuildingId, r.Name, r.Code, r.Floor, r.CreatedAt, r.UpdatedAt);
}
