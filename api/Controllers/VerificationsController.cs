using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Verification checks — did the repair actually hold?
///
/// [Authorize] with no policy on the controller and a policy per action, the same split as
/// WorkOrdersController: the reporter-facing reads and answers that will land here are not
/// a manager's, so a class-level FacilitiesManager policy would have to be undone for them.
///
/// THIS CONTROLLER DECIDES NOTHING. When a check falls due, what an answer means and when a
/// silent one goes to the agent are all C# in VerificationService.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VerificationsController : ControllerBase
{
    private readonly IVerificationService _verificationService;

    public VerificationsController(IVerificationService verificationService)
    {
        _verificationService = verificationService;
    }

    /// <summary>
    /// Runs one pass of the verification sweep now, rather than waiting for the timer.
    ///
    /// Not optional: Render's free tier sleeps an idle service, and a demo cannot wait an
    /// hour for VerificationSweepService to tick. It is the same pass the timer runs, under
    /// the same lock, so pressing it while the timer is mid-pass waits rather than doubling up.
    ///
    /// FacilitiesManager only, and like every one-role policy here an Admin is refused too.
    /// 200 even when some rows failed — a failed row is expired and counted in Failed, and
    /// the rest were processed.
    /// </summary>
    [HttpPost("run-sweep")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(VerificationSweepResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VerificationSweepResultDto>> RunSweep(CancellationToken cancellationToken)
    {
        return Ok(await _verificationService.ProcessDueChecksAsync(cancellationToken));
    }
}
