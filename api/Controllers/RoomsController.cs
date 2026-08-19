using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _roomService;

    public RoomsController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoomDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RoomDto>>> GetAll(
        [FromQuery] int? buildingId,
        CancellationToken cancellationToken)
    {
        var rooms = await _roomService.GetAllAsync(buildingId, cancellationToken);
        return Ok(rooms);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var room = await _roomService.GetByIdAsync(id, cancellationToken);
        return room is null ? NotFound() : Ok(room);
    }

    [HttpPost]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoomDto>> Create(
        CreateRoomDto dto,
        CancellationToken cancellationToken)
    {
        var created = await _roomService.CreateAsync(dto, cancellationToken);

        if (created is null)
        {
            ModelState.AddModelError(nameof(dto.BuildingId), $"Building {dto.BuildingId} does not exist.");
            return ValidationProblem(ModelState);
        }

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        CreateRoomDto dto,
        CancellationToken cancellationToken)
    {
        var updated = await _roomService.UpdateAsync(id, dto, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _roomService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
