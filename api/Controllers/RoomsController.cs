using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// The campus rooms. Reads for every signed-in user — the phone's room picker reads this
/// list — and writes for an Admin, the same split as the buildings and the asset registry.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _roomService;

    public RoomsController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoomDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<RoomDto>>> GetAll(
        [FromQuery] int? buildingId,
        CancellationToken cancellationToken)
    {
        var rooms = await _roomService.GetAllAsync(buildingId, cancellationToken);
        return Ok(rooms);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var room = await _roomService.GetByIdAsync(id, cancellationToken);
        return room is null ? NotFound() : Ok(room);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RoomDto>> Create(
        CreateRoomDto dto,
        CancellationToken cancellationToken)
    {
        var created = await _roomService.CreateAsync(dto, cancellationToken);

        if (created is null)
        {
            return BuildingNotFound(dto.BuildingId);
        }

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        CreateRoomDto dto,
        CancellationToken cancellationToken)
    {
        return await _roomService.UpdateAsync(id, dto, cancellationToken) switch
        {
            EstateWriteOutcome.Success => NoContent(),
            EstateWriteOutcome.NotFound => NotFound(),
            EstateWriteOutcome.BuildingNotFound => BuildingNotFound(dto.BuildingId),
            var outcome => throw new InvalidOperationException($"Unhandled room outcome '{outcome}'.")
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
        return await _roomService.DeleteAsync(id, cancellationToken) switch
        {
            EstateWriteOutcome.Success => NoContent(),
            EstateWriteOutcome.NotFound => NotFound(),
            EstateWriteOutcome.InUse => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Room is in use",
                Detail = $"Room {id} still has assets, reports or timetabled classes against it, " +
                         "and their history outlives the room."
            }),
            var outcome => throw new InvalidOperationException($"Unhandled room outcome '{outcome}'.")
        };
    }

    private ActionResult BuildingNotFound(int buildingId)
    {
        ModelState.AddModelError(nameof(CreateRoomDto.BuildingId), $"Building {buildingId} does not exist.");
        return ValidationProblem(ModelState);
    }
}
