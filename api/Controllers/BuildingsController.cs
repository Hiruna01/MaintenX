using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BuildingsController : ControllerBase
{
    private readonly IBuildingService _buildingService;

    public BuildingsController(IBuildingService buildingService)
    {
        _buildingService = buildingService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BuildingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BuildingDto>>> GetAll(CancellationToken cancellationToken)
    {
        var buildings = await _buildingService.GetAllAsync(cancellationToken);
        return Ok(buildings);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BuildingDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var building = await _buildingService.GetByIdAsync(id, cancellationToken);
        return building is null ? NotFound() : Ok(building);
    }

    [HttpPost]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BuildingDto>> Create(
        CreateBuildingDto dto,
        CancellationToken cancellationToken)
    {
        var created = await _buildingService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        CreateBuildingDto dto,
        CancellationToken cancellationToken)
    {
        var updated = await _buildingService.UpdateAsync(id, dto, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _buildingService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
