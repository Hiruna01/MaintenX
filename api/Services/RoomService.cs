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

    public async Task<EstateWriteOutcome> UpdateAsync(int id, CreateRoomDto dto, CancellationToken cancellationToken = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room is null)
        {
            return EstateWriteOutcome.NotFound;
        }

        var buildingExists = await _db.Buildings.AnyAsync(b => b.Id == dto.BuildingId, cancellationToken);
        if (!buildingExists)
        {
            return EstateWriteOutcome.BuildingNotFound;
        }

        room.BuildingId = dto.BuildingId;
        room.Name = dto.Name;
        room.Code = dto.Code;
        room.Floor = dto.Floor;

        await _db.SaveChangesAsync(cancellationToken);
        return EstateWriteOutcome.Success;
    }

    public async Task<EstateWriteOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room is null)
        {
            return EstateWriteOutcome.NotFound;
        }

        // Every one of these foreign keys is Restrict: the history outlives the room. Asked
        // first so the refusal is a 409 that says why, not a constraint violation as a 500.
        var inUse = await _db.Assets.AnyAsync(a => a.RoomId == id, cancellationToken)
                    || await _db.Reports.AnyAsync(r => r.RoomId == id, cancellationToken)
                    || await _db.ClassScheduleSlots.AnyAsync(c => c.RoomId == id, cancellationToken);

        if (inUse)
        {
            return EstateWriteOutcome.InUse;
        }

        _db.Rooms.Remove(room);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Something started pointing at the room between the check and the delete.
            _db.ChangeTracker.Clear();
            return EstateWriteOutcome.InUse;
        }

        return EstateWriteOutcome.Success;
    }

    private static RoomDto ToDto(Room r) =>
        new(r.Id, r.BuildingId, r.Name, r.Code, r.Floor, r.CreatedAt, r.UpdatedAt);
}
