using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IBuildingService
{
    Task<IEnumerable<BuildingDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<BuildingDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<BuildingDto> CreateAsync(CreateBuildingDto dto, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(int id, CreateBuildingDto dto, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
