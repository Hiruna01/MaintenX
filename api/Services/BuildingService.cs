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

    public async Task<BuildingDto> CreateAsync(CreateBuildingDto dto, CancellationToken cancellationToken = default)
    {
        var building = new Building
        {
            Name = dto.Name,
            Code = dto.Code
        };

        _db.Buildings.Add(building);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(building);
    }

    public async Task<bool> UpdateAsync(int id, CreateBuildingDto dto, CancellationToken cancellationToken = default)
    {
        var building = await _db.Buildings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (building is null)
        {
            return false;
        }

        building.Name = dto.Name;
        building.Code = dto.Code;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var building = await _db.Buildings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (building is null)
        {
            return false;
        }

        _db.Buildings.Remove(building);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static BuildingDto ToDto(Building b) =>
        new(b.Id, b.Name, b.Code, b.CreatedAt, b.UpdatedAt);
}
