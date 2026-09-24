using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Verification checks — did the repair actually hold?
///
/// [Authorize] with no policy on the controller and a policy per action, the same split as
/// WorkOrdersController: the reads and the answer below are a reporter's as much as a
/// manager's, so a class-level FacilitiesManager policy would have to be undone for them.
/// Which checks a caller may read, and whether they may answer one, is decided in
/// VerificationService from the token — nothing a client sends widens it.
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
    /// One page of checks. <paramref name="status"/> and <paramref name="assetId"/> are
    /// exact filters; <paramref name="dateFrom"/> and <paramref name="dateTo"/> bound DueAt
    /// as UTC calendar dates, BOTH ENDS INCLUSIVE. <paramref name="status"/> and
    /// <paramref name="sort"/> bind by enum NAME, so an unknown value is a 400 from model
    /// binding rather than a filter that silently matches nothing.
    ///
    /// WHO SEES WHAT IS NOT A QUERY PARAMETER. A Reporter gets the checks on their own
    /// reports and a FacilitiesManager or Admin gets them all, decided in the service.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<VerificationCheckDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<VerificationCheckDto>>> GetAll(
        [FromQuery] VerificationStatus? status,
        [FromQuery] int? assetId,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] VerificationSort sort = VerificationSort.DueAt,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCaller(out var callerId, out var callerRole))
        {
            return Unauthorized();
        }

        return Ok(await _verificationService.GetAllAsync(
            callerId, callerRole, status, assetId, dateFrom, dateTo,
            sort, page, pageSize, cancellationToken));
    }

    /// <summary>
    /// One check in full: the asset, the work order's claim (resolution note and completion
    /// time), the reporter's answer, and the agent's outcome and reason beside it.
    ///
    /// Scoped like the list. A check that exists but is on somebody else's report is a 403,
    /// told apart from a 404 by ExistsAsync — the same as GET /api/reports/{id}.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VerificationDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out var callerRole))
        {
            return Unauthorized();
        }

        var check = await _verificationService.GetDetailAsync(id, callerId, callerRole, cancellationToken);

        if (check is not null)
        {
            return Ok(check);
        }

        return await _verificationService.ExistsAsync(id, cancellationToken)
            ? NotYourCheck("Reporters see the checks on repairs to faults they reported. "
                         + "This one is on a report filed by somebody else.")
            : NotFound();
    }

    /// <summary>
    /// The reporter's answer to "Is the problem fixed?" — yes or no, and an optional comment
    /// of at most 300 characters. A FORM, NOT A CONVERSATION: one request, 204, and the
    /// check is finished. Nothing replies to the comment.
    ///
    /// Every rule behind it is C# in VerificationService.RecordReporterResponseAsync:
    /// 404 unknown check, 403 not the reporter of the linked report, 409 already answered,
    /// 409 not AwaitingReporterResponse. On success the answer, the status it implies and
    /// the hand-off to the agent are written together.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        int id,
        ReporterConfirmationDto dto,
        CancellationToken cancellationToken)
    {
        // Who answered is whoever the token says it is. The DTO carries no user id.
        if (!TryGetCaller(out var callerId, out _))
        {
            return Unauthorized();
        }

        var outcome = await _verificationService.RecordReporterResponseAsync(id, callerId, dto, cancellationToken);

        return outcome switch
        {
            ConfirmVerificationOutcome.Success => NoContent(),

            ConfirmVerificationOutcome.NotFound => NotFound(),

            ConfirmVerificationOutcome.NotTheReporter => NotYourCheck(
                "A repair is confirmed by the reporter who filed the fault, and this one was "
                + "filed by somebody else."),

            ConfirmVerificationOutcome.AlreadyAnswered => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Already answered",
                Detail = $"Verification check {id} has already been answered. It takes one "
                       + "answer, not a thread, and the first one stands."
            }),

            ConfirmVerificationOutcome.NotAwaitingResponse => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Not awaiting a response",
                Detail = $"Verification check {id} is not waiting on the reporter. Either it "
                       + "has not fallen due yet, or it was closed without an answer."
            }),

            // Unreachable, and deliberately loud rather than a quiet 500: a new outcome
            // added without a case here is a bug in this switch, not in the caller.
            _ => throw new InvalidOperationException($"Unhandled confirm outcome '{outcome}'.")
        };
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

    /// <summary>
    /// An ownership refusal, with a body: the policy-generated 403s elsewhere are role
    /// refusals, and the two read identically without one.
    /// </summary>
    private ObjectResult NotYourCheck(string detail) =>
        StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Not your report",
            Detail = detail
        });

    /// <summary>
    /// Who is asking, from the token and from nowhere else — the same reading as
    /// ReportsController. A token this API issued always carries both claims, so a failure
    /// here is a token it did not issue: 401.
    /// </summary>
    private bool TryGetCaller(out int callerId, out Role callerRole)
    {
        callerRole = default;

        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(sub, out callerId))
        {
            return false;
        }

        // Parsed by NAME, matching how AuthService writes it.
        return Enum.TryParse(User.FindFirst("role")?.Value, out callerRole);
    }
}
