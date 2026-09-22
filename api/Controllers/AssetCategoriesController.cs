using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Classes of equipment — projectors, air conditioners — and the defaults that apply to
/// everything in one.
///
/// Injects IAssetService rather than a service of its own: a category exists to classify
/// assets, the registry service already owns that table, and a second service over it
/// would be two places to keep the same rules. Same reads-for-everyone, writes-for-Admin
/// split as AssetsController, for the same reason — a picker on a report form needs this
/// list, and only an Admin decides what classes of equipment the estate recognises.
///
/// There is no DELETE. The foreign key from Asset is Restrict, so removing a category that
/// anything is filed under fails at the database; and a category with nothing under it is
/// not worth an endpoint and a role check to tidy away.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssetCategoriesController : ControllerBase
{
    private readonly IAssetService _assetService;

    public AssetCategoriesController(IAssetService assetService)
    {
        _assetService = assetService;
    }

    /// <summary>Every category, ordered by name. Not paginated — this list is short by nature.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AssetCategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<AssetCategoryDto>>> GetAll(CancellationToken cancellationToken)
    {
        var categories = await _assetService.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AssetCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetCategoryDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var category = await _assetService.GetCategoryByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : Ok(category);
    }

    /// <summary>
    /// Creates a category. Admin only. Null back from the service can only mean the name
    /// is taken — it is unique — so this one needs no pre-check to choose its status code.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(typeof(AssetCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetCategoryDto>> Create(
        CreateAssetCategoryDto dto,
        CancellationToken cancellationToken)
    {
        var created = await _assetService.CreateCategoryAsync(dto, cancellationToken);

        if (created is null)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Category name already in use",
                Detail = $"A category named '{dto.Name.Trim()}' already exists."
            });
        }

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Renames a category or changes its default warranty. Admin only.
    ///
    /// CategoryExistsAsync first: UpdateCategoryAsync returns false both for an id that
    /// does not exist and for a name already on another category, and those are a 404 and
    /// a 409. Same pattern as AssetsController.Create.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int id,
        CreateAssetCategoryDto dto,
        CancellationToken cancellationToken)
    {
        if (!await _assetService.CategoryExistsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var updated = await _assetService.UpdateCategoryAsync(id, dto, cancellationToken);

        if (!updated)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Category name already in use",
                Detail = $"Another category is already named '{dto.Name.Trim()}'."
            });
        }

        return NoContent();
    }
}
