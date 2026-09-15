using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class AssetService : IAssetService
{
    private readonly AppDbContext _db;

    public AssetService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<AssetDto>> GetAllAsync(
        int? roomId = null,
        int? assetCategoryId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Assets.AsNoTracking();

        if (roomId is not null)
        {
            query = query.Where(a => a.RoomId == roomId);
        }

        if (assetCategoryId is not null)
        {
            query = query.Where(a => a.AssetCategoryId == assetCategoryId);
        }

        return await query
            .OrderBy(a => a.AssetTag)
            .Select(a => ToDto(a))
            .ToListAsync(cancellationToken);
    }

    public Task<AssetDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        LoadDetailAsync(a => a.Id == id, cancellationToken);

    public Task<AssetDetailDto?> GetByTagAsync(string assetTag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetTag))
        {
            return Task.FromResult<AssetDetailDto?>(null);
        }

        // Trimmed because the tag arrives from a QR decode or a typed field, and a stray
        // space either side is a scanning artefact rather than a different asset.
        var tag = assetTag.Trim();

        return LoadDetailAsync(a => a.AssetTag == tag, cancellationToken);
    }

    public Task<bool> TagExistsAsync(string assetTag, CancellationToken cancellationToken = default)
    {
        var tag = (assetTag ?? string.Empty).Trim();
        return _db.Assets.AnyAsync(a => a.AssetTag == tag, cancellationToken);
    }

    public async Task<AssetDto?> CreateAsync(
        CreateAssetDto dto,
        CancellationToken cancellationToken = default)
    {
        var tag = dto.AssetTag.Trim();

        // All three checked here rather than left to the database, so a bad request is a
        // status code the caller can act on instead of a constraint violation out of the
        // driver. See the interface for how a controller tells them apart.
        if (await _db.Assets.AnyAsync(a => a.AssetTag == tag, cancellationToken))
        {
            return null;
        }

        if (!await _db.AssetCategories.AnyAsync(c => c.Id == dto.AssetCategoryId, cancellationToken))
        {
            return null;
        }

        if (!await _db.Rooms.AnyAsync(r => r.Id == dto.RoomId, cancellationToken))
        {
            return null;
        }

        var asset = new Asset
        {
            AssetTag = tag,
            Name = dto.Name,
            AssetCategoryId = dto.AssetCategoryId,
            RoomId = dto.RoomId,
            Manufacturer = dto.Manufacturer,
            Model = dto.Model,
            InstalledOn = dto.InstalledOn,
            WarrantyExpiresOn = dto.WarrantyExpiresOn,
            // Every asset enters the estate in service. CreateAssetDto has no Status field.
            Status = AssetStatus.Active
        };

        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(asset);
    }

    public async Task<bool> UpdateAsync(
        int id,
        UpdateAssetDto dto,
        CancellationToken cancellationToken = default)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null)
        {
            return false;
        }

        if (!await _db.AssetCategories.AnyAsync(c => c.Id == dto.AssetCategoryId, cancellationToken))
        {
            return false;
        }

        if (!await _db.Rooms.AnyAsync(r => r.Id == dto.RoomId, cancellationToken))
        {
            return false;
        }

        // AssetTag is not assigned from the DTO, which does not carry one: the printed
        // sticker and the QR code encode it, so it is fixed for the life of the row.
        asset.Name = dto.Name;
        asset.AssetCategoryId = dto.AssetCategoryId;
        asset.RoomId = dto.RoomId;
        asset.Manufacturer = dto.Manufacturer;
        asset.Model = dto.Model;
        asset.InstalledOn = dto.InstalledOn;
        asset.WarrantyExpiresOn = dto.WarrantyExpiresOn;
        asset.Status = dto.Status;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IEnumerable<AssetCategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.AssetCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => ToDto(c))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The one query behind both detail lookups — by id and by tag — so the two can never
    /// disagree about what "an asset with its history" includes.
    /// </summary>
    private async Task<AssetDetailDto?> LoadDetailAsync(
        System.Linq.Expressions.Expression<Func<Asset, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var asset = await _db.Assets
            .AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Room)
            .Include(a => a.ServiceRecords)
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (asset is null || asset.Category is null || asset.Room is null)
        {
            return null;
        }

        // Oldest first: this history is read to follow a machine over time, and a repeat
        // failure only reads as one in the order it happened. Id breaks ties inside a day.
        var history = asset.ServiceRecords
            .OrderBy(s => s.ServicedOn)
            .ThenBy(s => s.Id)
            .Select(ToDto)
            .ToList();

        return new AssetDetailDto(
            asset.Id,
            asset.AssetTag,
            asset.Name,
            ToDto(asset.Category),
            new RoomDto(
                asset.Room.Id,
                asset.Room.BuildingId,
                asset.Room.Name,
                asset.Room.Code,
                asset.Room.Floor,
                asset.Room.CreatedAt,
                asset.Room.UpdatedAt),
            asset.Manufacturer,
            asset.Model,
            asset.InstalledOn,
            asset.WarrantyExpiresOn,
            asset.Status,
            asset.CreatedAt,
            asset.UpdatedAt,
            history);
    }

    private static AssetDto ToDto(Asset a) =>
        new(a.Id, a.AssetTag, a.Name, a.AssetCategoryId, a.RoomId, a.Manufacturer, a.Model,
            a.InstalledOn, a.WarrantyExpiresOn, a.Status, a.CreatedAt, a.UpdatedAt);

    private static AssetCategoryDto ToDto(AssetCategory c) =>
        new(c.Id, c.Name, c.DefaultWarrantyMonths, c.CreatedAt, c.UpdatedAt);

    private static ServiceRecordDto ToDto(ServiceRecord s) =>
        new(s.Id, s.AssetId, s.ServicedOn, s.TechnicianName, s.TechnicianNote, s.Outcome,
            s.WorkOrderId, s.CreatedAt, s.UpdatedAt);
}
