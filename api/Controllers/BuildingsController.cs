using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// The campus buildings. Reads for every signed-in user — a reporter's room picker and the
/// agent's context both start here — and writes for an Admin, the same split as the asset
/// registry: deciding what the estate contains is an Admin's job.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BuildingsController : ControllerBase
{
    private readonly IBuildingService _buildingService;

    public BuildingsController(IBuildingService buildingService)
    {
        _buildingService = buildingService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BuildingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<BuildingDto>>> GetAll(CancellationToken cancellationToken)
    {
        var buildings = await _buildingService.GetAllAsync(cancellationToken);
        return Ok(buildings);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BuildingDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var building = await _buildingService.GetByIdAsync(id, cancellationToken);
        return building is null ? NotFound() : Ok(building);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingDto>> Create(
        CreateBuildingDto dto,
        CancellationToken cancellationToken)
    {
        var created = await _buildingService.CreateAsync(dto, cancellationToken);

        return created is null
            ? CodeTaken(dto.Code)
            : CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

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
        CreateBuildingDto dto,
        CancellationToken cancellationToken)
    {
        return await _buildingService.UpdateAsync(id, dto, cancellationToken) switch
        {
            EstateWriteOutcome.Success => NoContent(),
            EstateWriteOutcome.NotFound => NotFound(),
            EstateWriteOutcome.CodeTaken => CodeTaken(dto.Code),
            var outcome => throw new InvalidOperationException($"Unhandled building outcome '{outcome}'.")
        };
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        return await _buildingService.DeleteAsync(id, cancellationToken) switch
        {
            EstateWriteOutcome.Success => NoContent(),
            EstateWriteOutcome.NotFound => NotFound(),
            EstateWriteOutcome.InUse => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Building still has rooms",
                Detail = $"Building {id} still has rooms. Move or delete them first."
            }),
            var outcome => throw new InvalidOperationException($"Unhandled building outcome '{outcome}'.")
        };
    }

    private ObjectResult CodeTaken(string code) => Conflict(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Building code already in use",
        Detail = $"Another building already has the code '{code}'."
    });
}
