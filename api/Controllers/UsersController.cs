using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Users, read by role — today only so a manager has a list of technicians to assign work
/// orders to and filter the dispatch board by. Without it, assigning means typing a user id.
///
/// FacilitiesManager only, the same policy as assign: the one person who picks a technician
/// is the one who may list them. An Admin is refused, as on every manager action — the
/// policies are one-per-Role and carry no seniority.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = nameof(Role.FacilitiesManager))]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Every user with <paramref name="role"/>, by name. The role is REQUIRED — a missing one
    /// is a 400, not the whole user table — and binds by enum NAME, so an unknown role is a
    /// 400 from model binding too.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetByRole(
        [FromQuery, Required] Role? role,
        CancellationToken cancellationToken)
    {
        var users = await _userService.GetByRoleAsync(role!.Value, cancellationToken);
        return Ok(users);
    }
}
