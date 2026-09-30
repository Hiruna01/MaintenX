using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Accounts. Managing them — list, read, create, edit, deactivate, reactivate, reset a
/// password — is an Admin's job and nobody else's, the same rule that makes an Admin the only
/// one who can register a staff account. The one read that is not an Admin's is the
/// technician picker, which a FacilitiesManager needs to assign work.
///
/// `[Authorize]` on the class with a policy per action, like the asset registry, so 401 (no
/// token) and 403 (wrong role) stay separately demonstrable on one controller.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Every active Technician — the picker behind assign and the board's filter. A fixed
    /// route rather than a role parameter, so there is still no way for a manager to list
    /// the whole user table.
    /// </summary>
    [HttpGet("technicians")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetTechnicians(CancellationToken cancellationToken)
    {
        var users = await _userService.GetActiveTechniciansAsync(cancellationToken);
        return Ok(users);
    }

    [HttpGet]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(typeof(PagedResult<UserAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserAdminDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] Role? role,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _userService.GetAllAsync(search, role, isActive, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAdminDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserAdminDto>> Create(CreateUserDto dto, CancellationToken cancellationToken)
    {
        var created = await _userService.CreateAsync(dto, cancellationToken);

        return created is null
            ? EmailTaken(dto.Email)
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
    public async Task<IActionResult> Update(int id, UpdateUserDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var outcome = await _userService.UpdateAsync(id, callerId, dto, cancellationToken);
        return outcome == UserWriteOutcome.EmailTaken ? EmailTaken(dto.Email) : ToResult(id, outcome);
    }

    /// <summary>
    /// Deactivates — never deletes. The verb is what the client means ("this person is gone");
    /// how the system honours it without breaking every report, answer and work order that
    /// names them is its own business, the same as DELETE on an asset retiring it.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return ToResult(id, await _userService.DeactivateAsync(id, callerId, cancellationToken));
    }

    [HttpPost("{id:int}/reactivate")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken)
    {
        return ToResult(id, await _userService.ReactivateAsync(id, cancellationToken));
    }

    [HttpPost("{id:int}/password")]
    [Authorize(Policy = nameof(Role.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordDto dto, CancellationToken cancellationToken)
    {
        return ToResult(id, await _userService.ResetPasswordAsync(id, dto, cancellationToken));
    }

    private IActionResult ToResult(int id, UserWriteOutcome outcome) => outcome switch
    {
        UserWriteOutcome.Success => NoContent(),
        UserWriteOutcome.NotFound => NotFound(),
        UserWriteOutcome.OwnAccount => Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "You cannot do that to your own account",
            Detail = "An Admin cannot deactivate their own account or change their own role. "
                     + "Ask another Admin — that is what keeps at least one Admin able to sign in."
        }),
        UserWriteOutcome.HasLiveWork => Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Technician still has work assigned",
            Detail = $"User {id} has unfinished work orders assigned, and only the assigned "
                     + "Technician can complete them. Reassign those orders first."
        }),
        _ => throw new InvalidOperationException($"Unhandled user outcome '{outcome}'.")
    };

    private ObjectResult EmailTaken(string email) => Conflict(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Email already in use",
        Detail = $"Another account already uses '{email.Trim().ToLowerInvariant()}'."
    });

    private bool TryGetCallerId(out int callerId) =>
        int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out callerId);
}
