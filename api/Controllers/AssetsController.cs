using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// The asset registry — what equipment exists, where it is, and what has been done to it.
///
/// [Authorize] on the class, with no policy: reading the registry is something any
/// signed-in user does. A technician looks up a machine's history before a visit and a
/// reporter scans a sticker, so a role check on the reads would break both.
///
/// Writing is Admin only, applied per-action below. That is what keeps 401 and 403
/// distinct here and answerable separately: no token at all is 401 on every action, and a
/// valid Reporter token is 200 on a read and 403 on a write.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _assetService;

    public AssetsController(IAssetService assetService)
    {
        _assetService = assetService;
    }

    /// <summary>
    /// One page of assets. <paramref name="search"/> matches the name or the tag;
    /// the three filters are exact matches and all of them combine.
    ///
    /// <paramref name="status"/> and <paramref name="sort"/> bind by enum NAME
    /// ("UnderMaintenance", "InstalledOn") — the same strings the JSON contract and the
    /// database use — so a client never sends an ordinal, and a name that is not a member
    /// is a 400 from model binding rather than a filter that silently matches nothing.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AssetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<AssetDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] int? roomId,
        [FromQuery] AssetStatus? status,
        [FromQuery] AssetSort sort = AssetSort.Name,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _assetService.GetAllAsync(
            search, categoryId, roomId, status, sort, page, pageSize, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var asset = await _assetService.GetByIdAsync(id, cancellationToken);
        return asset is null ? NotFound() : Ok(asset);
    }

    /// <summary>
    /// The QR path: a scan yields the string printed on the sticker and nothing else, so
    /// this is the only lookup a reporter standing in front of a machine can perform.
    ///
    /// An unknown tag is a 404 like any other miss — a sticker from some other system, or
    /// one belonging to an asset that was never registered here, is not an error.
    /// </summary>
    [HttpGet("by-tag/{assetTag}")]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDetailDto>> GetByTag(
        string assetTag,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.GetByTagAsync(assetTag, cancellationToken);
        return asset is null ? NotFound() : Ok(asset);
    }

    /// <summary>
    /// What an asset's history adds up to as of today — the registry's business operation,
    /// and the one endpoint here that is not CRUD.
    ///
    /// It is also NOT an agent call. Every figure it returns is a count or a date
    /// comparison performed in C#: how often this machine has failed, whether those
    /// failures are clustering, whether it is still under warranty. Those are deterministic
    /// business rules, so they live in C# and never in a prompt — the diagnostic agent
    /// reads this summary, it does not produce it.
    /// </summary>
    [HttpGet("{id:int}/failure-summary")]
    [ProducesResponseType(typeof(AssetFailureSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetFailureSummaryDto>> GetFailureSummary(
        int id,
        CancellationToken cancellationToken)
    {
        var summary = await _assetService.GetFailureSummaryAsync(id, cancellationToken);
        return summary is null ? NotFound() : Ok(summary);
    }

    /// <summary>
    /// Registers an asset. Admin only — a new row here means a new sticker printed and
    /// applied, which is an estate management decision, not a reporting one.
    ///
    /// CreateAsync returns null for three different reasons and there is no Result wrapper
    /// in this project, so TagExistsAsync is called first to tell a 409 from a 400. Same
    /// shape as InternalToolsController calling IWorkflowService.ExistsAsync before
    /// choosing its status code.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetDto>> Create(
        CreateAssetDto dto,
        CancellationToken cancellationToken)
    {
        if (await _assetService.TagExistsAsync(dto.AssetTag, cancellationToken))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Asset tag already in use",
                Detail = $"An asset is already registered with the tag '{dto.AssetTag.Trim()}'. " +
                         "Tags are unique across the estate because a QR scan yields nothing else."
            });
        }

        var created = await _assetService.CreateAsync(dto, cancellationToken);

        if (created is null)
        {
            // The tag was free a moment ago, so what is left is a bad reference. Both are
            // named rather than guessed between — the caller sent both values.
            ModelState.AddModelError(
                nameof(dto.AssetCategoryId),
                $"Category {dto.AssetCategoryId} or room {dto.RoomId} does not exist.");

            return ValidationProblem(ModelState);
        }

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Edits an asset. Admin only. The tag is not editable and UpdateAssetDto carries no
    /// field for it — renaming the row would orphan every sticker already applied.
    ///
    /// ExistsAsync first, for the same reason as Create: UpdateAsync returns false for a
    /// missing asset and for a bad category or room alike, and those are 404 and 400.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        UpdateAssetDto dto,
        CancellationToken cancellationToken)
    {
        if (!await _assetService.ExistsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var updated = await _assetService.UpdateAsync(id, dto, cancellationToken);

        if (!updated)
        {
            ModelState.AddModelError(
                nameof(dto.AssetCategoryId),
                $"Category {dto.AssetCategoryId} or room {dto.RoomId} does not exist.");

            return ValidationProblem(ModelState);
        }

        return NoContent();
    }

    /// <summary>
    /// Takes an asset out of the estate. Admin only, 204 on success, 404 for an id that
    /// does not exist.
    ///
    /// THIS DOES NOT DELETE THE ROW — it sets the status to Retired, and the service
    /// method behind it is called RetireAsync for that reason. An asset carries the service
    /// history the diagnostic agent reads, and that history outlives the machine it
    /// describes: an asset scrapped last year is still the evidence for why its replacement
    /// was bought. Every foreign key into the registry is Restrict, so a real delete would
    /// not quietly take that history with it — it would fail out of the driver for any
    /// asset that has ever been serviced, reported on or worked on, which is every asset
    /// worth deleting. Retired is how equipment leaves the estate.
    ///
    /// DELETE is still the right verb for the client: "this equipment is gone" is what the
    /// caller means, and how the registry honours that without losing the history is the
    /// registry's business, not theirs.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var retired = await _assetService.RetireAsync(id, cancellationToken);
        return retired ? NoContent() : NotFound();
    }
}
