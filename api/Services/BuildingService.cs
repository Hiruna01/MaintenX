using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class BuildingService : IBuildingService
{
    private readonly AppDbContext _db;

    public BuildingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<BuildingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Buildings
            .AsNoTracking()
            .OrderBy(b => b.Code)
            .Select(b => ToDto(b))
            .ToListAsync(cancellationToken);
    }

    public async Task<BuildingDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var building = await _db.Buildings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        return building is null ? null : ToDto(building);
    }

    public async Task<BuildingDto?> CreateAsync(CreateBuildingDto dto, CancellationToken cancellationToken = default)
    {
        // Checked first so a duplicate is a 409 naming the problem. The unique index on Code
        // is still the real guard — two requests can both pass this check — so a failed save
        // is re-checked below rather than surfacing as a 500.
        if (await CodeTakenAsync(dto.Code, exceptId: null, cancellationToken))
        {
            return null;
        }

        var building = new Building
        {
            Name = dto.Name,
            Code = dto.Code
        };

        _db.Buildings.Add(building);

        if (!await TrySaveAsync(dto.Code, exceptId: null, cancellationToken))
        {
            return null;
        }

        return ToDto(building);
    }

    public async Task<EstateWriteOutcome> UpdateAsync(int id, CreateBuildingDto dto, CancellationToken cancellationToken = default)
    {
        var building = await _db.Buildings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (building is null)
        {
            return EstateWriteOutcome.NotFound;
        }

        if (await CodeTakenAsync(dto.Code, exceptId: id, cancellationToken))
        {
            return EstateWriteOutcome.CodeTaken;
        }

        building.Name = dto.Name;
        building.Code = dto.Code;

        return await TrySaveAsync(dto.Code, exceptId: id, cancellationToken)
            ? EstateWriteOutcome.Success
            : EstateWriteOutcome.CodeTaken;
    }

    public async Task<EstateWriteOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var building = await _db.Buildings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (building is null)
        {
            return EstateWriteOutcome.NotFound;
        }

        // Rooms cascade from their building, and assets, reports and classes are Restrict
        // from their room — so deleting a building with rooms would either take the rooms
        // with it or fail in the driver. Only an empty building goes.
        if (await _db.Rooms.AnyAsync(r => r.BuildingId == id, cancellationToken))
        {
            return EstateWriteOutcome.InUse;
        }

        _db.Buildings.Remove(building);
        await _db.SaveChangesAsync(cancellationToken);
        return EstateWriteOutcome.Success;
    }

    private Task<bool> CodeTakenAsync(string code, int? exceptId, CancellationToken cancellationToken) =>
        _db.Buildings.AnyAsync(b => b.Code == code && b.Id != exceptId, cancellationToken);

    /// <summary>
    /// Saves, turning a lost race on the unique Code index into false instead of a 500. Any
    /// other database failure is rethrown for the exception middleware.
    /// </summary>
    private async Task<bool> TrySaveAsync(string code, int? exceptId, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Detached first, so the re-check reads the database and not the failed insert.
            _db.ChangeTracker.Clear();

            if (await CodeTakenAsync(code, exceptId, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    private static BuildingDto ToDto(Building b) =>
        new(b.Id, b.Name, b.Code, b.CreatedAt, b.UpdatedAt);
}
