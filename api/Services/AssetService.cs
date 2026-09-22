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

    /// <summary>Largest page a client may ask for, so one request cannot pull the estate.</summary>
    private const int MaxPageSize = 100;

    public async Task<PagedResult<AssetDto>> GetAllAsync(
        string? search = null,
        int? assetCategoryId = null,
        int? roomId = null,
        AssetStatus? status = null,
        AssetSort sort = AssetSort.Name,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Clamp rather than reject, the same as WorkflowService: a client asking for page
        // 0 gets page 1, not a 400.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);

        var query = _db.Assets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLower().Contains(), not EF.Functions.ILike(): ILike is Npgsql-only, and the
            // tests run on SQLite by default, so an Npgsql-only call would be a runtime
            // failure nothing on a developer's machine would catch. This translates to
            // lower(x) LIKE '%...%' on both providers, which is the point — SQLite's LIKE
            // is already case-insensitive and PostgreSQL's is not, so lowering both sides
            // is what makes the two behave the same.
            var term = search.Trim().ToLower();

            query = query.Where(a =>
                a.Name.ToLower().Contains(term) || a.AssetTag.ToLower().Contains(term));
        }

        if (assetCategoryId is not null)
        {
            query = query.Where(a => a.AssetCategoryId == assetCategoryId);
        }

        if (roomId is not null)
        {
            query = query.Where(a => a.RoomId == roomId);
        }

        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        // Counted before paging, so TotalCount describes the whole filtered set rather
        // than the slice being returned.
        var totalCount = await query.CountAsync(cancellationToken);

        // Id breaks every tie. Without it two assets sharing a name — or an installation
        // date, which many do, since equipment arrives in batches — could order differently
        // between two queries, and a row would appear on both page 1 and page 2 or on
        // neither. A paged list needs a total order, not just a sorted column.
        query = sort switch
        {
            AssetSort.InstalledOn => query.OrderBy(a => a.InstalledOn).ThenBy(a => a.Id),
            _ => query.OrderBy(a => a.Name).ThenBy(a => a.Id)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => ToDto(a))
            .ToListAsync(cancellationToken);

        return new PagedResult<AssetDto>(items, page, pageSize, totalCount);
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

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Assets.AnyAsync(a => a.Id == id, cancellationToken);

    public async Task<bool> RetireAsync(int id, CancellationToken cancellationToken = default)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null)
        {
            return false;
        }

        // NOT _db.Assets.Remove(asset). The row stays; only its status changes.
        //
        // The asset's service history is what the diagnostic agent reads, and it outlives
        // the machine it describes — an asset scrapped last year is still the evidence for
        // why its replacement was bought. Every foreign key into the registry is Restrict,
        // so a real delete would not quietly take that history with it, it would throw out
        // of the driver as a 500 for any asset that has ever been serviced, reported on or
        // worked on. Retired is how equipment leaves the estate.
        //
        // Already Retired falls through to a save that changes nothing and still reports
        // success: the caller asked for this asset to be out of service, and it is.
        asset.Status = AssetStatus.Retired;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AssetFailureSummaryDto?> GetFailureSummaryAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var asset = await _db.Assets
            .AsNoTracking()
            .Include(a => a.ServiceRecords)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (asset is null)
        {
            return null;
        }

        // Every figure below is computed here in C#, over rows already materialised —
        // counts and date comparisons, nothing else. Two reasons, and both matter:
        //
        //   * These are deterministic business rules. A failure count or a warranty date
        //     is arithmetic, and arithmetic a model performs is unauditable. The agent may
        //     READ this summary through a tool call; it never produces one.
        //   * DateOnly arithmetic does not translate to SQLite SQL, where dates are TEXT.
        //     Doing it in memory behaves identically on both test databases — the same
        //     reasoning as evaluating the approval threshold in C#.
        //
        // The history of one asset is a handful of rows, so pulling it is not a cost.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var twelveMonthsAgo = today.AddYears(-1);

        // Ninety days, and IsRepeatFailure below reads this SAME cut-off rather than a
        // second one of its own. AddMonths(-3) would be a window of 89, 90, 91 or 92 days
        // depending on the month, and a summary reporting two visits this quarter next to
        // a repeat-failure flag would be read as a bug — and would be one.
        var ninetyDaysAgo = today.AddDays(-90);

        var history = asset.ServiceRecords;

        var failureCount12Months = history.Count(s => s.ServicedOn >= twelveMonthsAgo);
        var failureCount3Months = history.Count(s => s.ServicedOn >= ninetyDaysAgo);

        // Max over an empty history is an exception, not a null, so the emptiness is
        // checked rather than caught.
        DateOnly? lastServicedOn = history.Count == 0
            ? null
            : history.Max(s => s.ServicedOn);

        var daysSinceLastService = lastServicedOn is null
            ? (int?)null
            : today.DayNumber - lastServicedOn.Value.DayNumber;

        return new AssetFailureSummaryDto(
            asset.Id,
            asset.AssetTag,
            failureCount12Months,
            failureCount3Months,
            lastServicedOn,
            daysSinceLastService,
            history.Count(s => s.Outcome == ServiceOutcome.TemporaryFix),
            // A null WarrantyExpiresOn is "none recorded", which is not "expired" but is
            // not cover either. Today counts as covered — a warranty runs to the end of
            // the day it expires on, which is exactly why this column is a DateOnly.
            asset.WarrantyExpiresOn is not null && asset.WarrantyExpiresOn >= today,
            // The whole rule, in one line, where anyone can read it: three or more visits
            // inside ninety days. Not a prompt, not a model's judgement, and not a number
            // that changes between two calls with the same history.
            failureCount3Months >= 3,
            history
                .Select(s => s.Outcome)
                .Distinct()
                .OrderBy(o => o)
                .ToList());
    }

    // -----------------------------------------------------------------------
    // Reads behind the agent tools. Ordinary queries on the same DbContext as everything
    // else here — the agent has no database credentials of its own and reaches these only
    // through the allow-listed tool router.
    // -----------------------------------------------------------------------

    public async Task<AssetContextDto?> GetAssetContextAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var asset = await _db.Assets
            .AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Room)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (asset is null || asset.Category is null || asset.Room is null)
        {
            return null;
        }

        return new AssetContextDto(ToDto(asset), asset.Category.Name, asset.Room.Name);
    }

    public async Task<IReadOnlyList<ServiceRecordDto>?> GetRecentServiceHistoryAsync(
        int assetId,
        CancellationToken cancellationToken = default)
    {
        // Asked separately, and the answer matters: without this, an unknown asset id and
        // an asset that has never been serviced both come back as an empty list, and the
        // agent would read "this machine has a clean record" off a machine that does not
        // exist. Null here becomes found=false in the tool response.
        if (!await _db.Assets.AnyAsync(a => a.Id == assetId, cancellationToken))
        {
            return null;
        }

        // Newest first, unlike the detail read — see the interface. Id breaks ties inside
        // a day, descending for the same reason the date does.
        return await _db.ServiceRecords
            .AsNoTracking()
            .Where(s => s.AssetId == assetId)
            .OrderByDescending(s => s.ServicedOn)
            .ThenByDescending(s => s.Id)
            .Take(IAssetService.MaxToolHistoryRows)
            .Select(s => ToDto(s))
            .ToListAsync(cancellationToken);
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

    public async Task<AssetCategoryDto?> GetCategoryByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var category = await _db.AssetCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return category is null ? null : ToDto(category);
    }

    public async Task<AssetCategoryDto?> CreateCategoryAsync(
        CreateAssetCategoryDto dto,
        CancellationToken cancellationToken = default)
    {
        var name = dto.Name.Trim();

        // Checked here rather than left to the unique index, so a duplicate is a status
        // code the caller can act on instead of a constraint violation out of the driver.
        // The index stays: it is what makes the rule true of the database rather than only
        // of this method, and two requests racing each other still cannot both win.
        if (await _db.AssetCategories.AnyAsync(c => c.Name == name, cancellationToken))
        {
            return null;
        }

        var category = new AssetCategory
        {
            Name = name,
            DefaultWarrantyMonths = dto.DefaultWarrantyMonths
        };

        _db.AssetCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(category);
    }

    public async Task<bool> UpdateCategoryAsync(
        int id,
        CreateAssetCategoryDto dto,
        CancellationToken cancellationToken = default)
    {
        var category = await _db.AssetCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return false;
        }

        var name = dto.Name.Trim();

        // c.Id != id, so renaming a category to the name it already has is an ordinary
        // update rather than a conflict with itself.
        if (await _db.AssetCategories.AnyAsync(c => c.Id != id && c.Name == name, cancellationToken))
        {
            return false;
        }

        category.Name = name;

        // Changing this does not touch any existing asset: DefaultWarrantyMonths is a
        // default for data entry, and an asset's own WarrantyExpiresOn is what every
        // warranty decision reads — a unit may have been bought on different terms.
        category.DefaultWarrantyMonths = dto.DefaultWarrantyMonths;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> CategoryExistsAsync(int id, CancellationToken cancellationToken = default) =>
        _db.AssetCategories.AnyAsync(c => c.Id == id, cancellationToken);

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
