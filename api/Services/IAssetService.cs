using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IAssetService
{
    /// <summary>
    /// One page of assets. Every argument is optional and they all combine: a free-text
    /// search over name and tag, three exact-match filters, and the column to order by.
    ///
    /// Paged with <see cref="PagedResult{T}"/>, the one pagination type in this project.
    /// Page and page size are CLAMPED rather than rejected — a client asking for page 0
    /// gets page 1, not a 400 — and the page size has a ceiling so one request cannot pull
    /// the whole estate.
    /// </summary>
    Task<PagedResult<AssetDto>> GetAllAsync(
        string? search = null,
        int? assetCategoryId = null,
        int? roomId = null,
        AssetStatus? status = null,
        AssetSort sort = AssetSort.Name,
        int page = 1,
        int pageSize = 20,
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
    ///
    /// Three reasons, one signal again, so a controller that needs to tell a 404 (no such
    /// asset) from a 400 (bad category or room) calls <see cref="ExistsAsync"/> first.
    /// </summary>
    Task<bool> UpdateAsync(int id, UpdateAssetDto dto, CancellationToken cancellationToken = default);

    /// <summary>True when the asset exists. Lets a caller tell a 404 from a 400.</summary>
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes an asset out of the estate by setting its status to
    /// <see cref="AssetStatus.Retired"/>. Returns false when no asset has that id.
    ///
    /// THIS DOES NOT DELETE THE ROW, and the method is named for what it does rather than
    /// for the verb on the endpoint in front of it. An asset carries the service history
    /// the diagnostic agent reads, and that history outlives the working life of the
    /// machine it describes; every foreign key into the registry is Restrict precisely so
    /// that removing the row is impossible rather than merely discouraged. Retiring is
    /// what "this equipment has left the estate" means here.
    ///
    /// Retiring an asset that is already Retired is a no-op and still returns true: the
    /// caller asked for it to be out of service, and it is.
    /// </summary>
    Task<bool> RetireAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The registry's business operation: what an asset's history adds up to as of today.
    /// Returns null when no asset has that id.
    ///
    /// Counts and date comparisons, computed in C# over the materialised history — never
    /// an agent call, and never a figure a model produced. See AssetFailureSummaryDto.
    /// </summary>
    Task<AssetFailureSummaryDto?> GetFailureSummaryAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Every category, ordered by name. Used to populate a picker.</summary>
    Task<IEnumerable<AssetCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>One category, or null when no category has that id (a 404).</summary>
    Task<AssetCategoryDto?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a category. Returns null when the name is already taken — the name is
    /// unique, because two categories called "Projector" make a picker nobody can choose
    /// from correctly and a filter that silently returns half the estate. One reason, so
    /// null needs no pre-check here: it can only mean 409.
    /// </summary>
    Task<AssetCategoryDto?> CreateCategoryAsync(
        CreateAssetCategoryDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns false when the category does not exist OR when the new name is already on a
    /// different category. Same pattern as the asset writes: a controller telling a 404
    /// from a 409 calls <see cref="CategoryExistsAsync"/> first.
    /// </summary>
    Task<bool> UpdateCategoryAsync(
        int id,
        CreateAssetCategoryDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>True when the category exists. Lets a caller tell a 404 from a 409.</summary>
    Task<bool> CategoryExistsAsync(int id, CancellationToken cancellationToken = default);
}
