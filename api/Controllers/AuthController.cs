using System.IdentityModel.Tokens.Jwt;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Creates an account. Open to anonymous callers, but only for the Reporter role — that
    /// is self-registration from the phone. Any other role needs an Admin's token: without
    /// one the request is a 403, whatever role the body asks for. See AuthService.RegisterAsync.
    ///
    /// [AllowAnonymous] skips authorization, not authentication: a token that is sent is
    /// still read, which is how an Admin's request is told apart from a stranger's.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        Role? callerRole = Enum.TryParse<Role>(User.FindFirst("role")?.Value, out var parsed) ? parsed : null;

        var result = await _authService.RegisterAsync(request, callerRole, cancellationToken);

        return result.Outcome switch
        {
            RegisterOutcome.Registered => CreatedAtAction(nameof(Me), value: result.Response),
            RegisterOutcome.EmailTaken => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Email already registered.",
                Detail = "An account with that email address already exists."
            }),
            RegisterOutcome.RoleNotAllowed => StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Role not allowed.",
                Detail = "Anyone may register as a Reporter. Every other role is created by an Admin."
            }),
            _ => throw new InvalidOperationException($"Unhandled register outcome '{result.Outcome}'.")
        };
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _authService.LoginAsync(request, cancellationToken);

        // Unknown email and wrong password give the same 401 on purpose — a different
        // response for each would let anyone test which addresses have accounts.
        return response is null ? Unauthorized() : Ok(response);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(sub, out var userId))
        {
            return Unauthorized();
        }

        var user = await _authService.GetByIdAsync(userId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    /// <summary>
    /// Exists purely to demonstrate role-based authorization. A caller with no token gets
    /// 401 (we do not know who you are); a caller holding a valid Reporter token gets 403
    /// (we know who you are, and you are not allowed). Those are different answers and
    /// ASP.NET Core returns them without any code here.
    /// </summary>
    [HttpGet("manager-only")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult ManagerOnly()
    {
        return Ok(new { message = "You are a Facilities Manager." });
    }
}
