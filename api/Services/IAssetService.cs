using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IAssetService
{
    /// <summary>
    /// Assets, ordered by tag. Both filters are optional and combine.
    /// </summary>
    Task<IEnumerable<AssetDto>> GetAllAsync(
        int? roomId = null,
        int? assetCategoryId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One asset with its category, room and full service history. Returns null when no
    /// asset has that id (a 404 for the caller).
    /// </summary>
    Task<AssetDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same detail, looked up by the tag printed on the equipment — this is the QR
    /// scan path, where the only thing the caller has is the string off the sticker.
    /// Returns null when the tag is unknown (a 404), which is what a scan of a sticker
    /// from some other system produces.
    /// </summary>
    Task<AssetDetailDto?> GetByTagAsync(string assetTag, CancellationToken cancellationToken = default);

    /// <summary>True when the tag is already in use. See CreateAsync.</summary>
    Task<bool> TagExistsAsync(string assetTag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an asset. Returns null when the referenced category or room does not
    /// exist, OR when the tag is already taken — three reasons, one signal, because there
    /// is no Result wrapper in this project.
    ///
    /// A controller that needs to tell a 409 (duplicate tag) from a 400 (bad reference)
    /// calls <see cref="TagExistsAsync"/> first, the same way InternalToolsController
    /// calls IWorkflowService.ExistsAsync before deciding its status code.
    /// </summary>
    Task<AssetDto?> CreateAsync(CreateAssetDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns false when the asset, or the category or room it is being moved to, does
    /// not exist. The tag cannot be changed — see UpdateAssetDto.
    /// </summary>
    Task<bool> UpdateAsync(int id, UpdateAssetDto dto, CancellationToken cancellationToken = default);

    /// <summary>Every category, ordered by name. Used to populate a picker.</summary>
    Task<IEnumerable<AssetCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
}
