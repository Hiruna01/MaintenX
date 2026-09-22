using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Maintenance reports. This is the endpoint the Flutter client posts to.
///
/// [Authorize] with no policy: any signed-in user may report a fault — that is the point
/// of the Reporter role — so there is no role check here, only an identity one. No token
/// is a 401; the reporter's identity then comes from that token and from nowhere else.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IClarificationService _clarificationService;

    public ReportsController(
        IReportService reportService,
        IClarificationService clarificationService)
    {
        _reportService = reportService;
        _clarificationService = clarificationService;
    }

    /// <summary>
    /// One page of reports. <paramref name="search"/> matches the description; the rest are
    /// exact filters and all of them combine. <paramref name="dateFrom"/> and
    /// <paramref name="dateTo"/> are calendar dates and BOTH ENDS ARE INCLUSIVE.
    ///
    /// <paramref name="status"/> and <paramref name="sort"/> bind by enum NAME
    /// ("AwaitingClarification", "CreatedAt") — the same strings the JSON contract and the
    /// database use — so a client never sends an ordinal, and a name that is not a member is
    /// a 400 from model binding rather than a filter that silently matches nothing.
    ///
    /// WHO SEES WHAT IS NOT A QUERY PARAMETER. A Reporter gets their own reports and a
    /// FacilitiesManager or Admin gets the estate, decided in ReportService from the token's
    /// role — there is no parameter here that widens it and nothing a client can send that
    /// changes it. The controller passes on who is asking; it does not decide what that
    /// means.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ReportListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<ReportListItemDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] ReportStatus? status,
        [FromQuery] int? roomId,
        [FromQuery] int? assetId,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] ReportSort sort = ReportSort.CreatedAt,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCaller(out var callerId, out var callerRole))
        {
            return Unauthorized();
        }

        var result = await _reportService.GetAllAsync(
            callerId, callerRole, search, status, roomId, assetId, dateFrom, dateTo,
            sort, page, pageSize, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// One report in full: its room and asset resolved, the clarification questions with
    /// whatever answers have come back, and every agent step recorded for it.
    ///
    /// Scoped exactly like the list — a Reporter reads their own reports and nobody else's.
    /// A report that exists but is not the caller's is a 403, matching
    /// POST {id}/clarifications on the same resource: the token is valid and we know who
    /// they are, so this is a refusal, not a question about their identity. ExistsAsync
    /// tells that apart from a genuine 404, the same way AssetsController calls
    /// TagExistsAsync before choosing its status code.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ReportDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out var callerRole))
        {
            return Unauthorized();
        }

        var report = await _reportService.GetDetailAsync(id, callerId, callerRole, cancellationToken);

        if (report is not null)
        {
            return Ok(report);
        }

        return await _reportService.ExistsAsync(id, cancellationToken)
            ? StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Not your report",
                Detail = "Reporters see the reports they filed. This one was filed by somebody else."
            })
            : NotFound();
    }

    /// <summary>
    /// Moves a report along its lifecycle. FacilitiesManager only — deciding that a fault is
    /// diagnosed, or that it is finished with, is a management call, and it is what keeps
    /// 401 and 403 separately demonstrable on this controller: no token is 401 everywhere,
    /// while a Reporter's token is 200 on the reads above and 403 here.
    ///
    /// AN ILLEGAL MOVE IS A 409, NEVER A QUIET SUCCESS. The legal moves are a hardcoded map
    /// in ReportService and reading it is the whole rule; a status that could go anywhere
    /// would make the lifecycle advisory. 409 rather than 400 because nothing about the
    /// request is malformed — the value is a real member of the enum, and it is the report
    /// that is not where the caller thinks it is.
    ///
    /// PATCH, not PUT: this changes one field and leaves the reporter's own account of the
    /// fault alone. 204, like every other update in this API.
    /// </summary>
    [HttpPatch("{id:int}/status")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        int id,
        UpdateReportStatusDto dto,
        CancellationToken cancellationToken)
    {
        var outcome = await _reportService.UpdateStatusAsync(id, dto.Status, cancellationToken);

        return outcome switch
        {
            UpdateReportStatusOutcome.Success => NoContent(),
            UpdateReportStatusOutcome.NotFound => NotFound(),
            UpdateReportStatusOutcome.IllegalTransition => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Illegal status transition",
                Detail = $"Report {id} cannot move to {dto.Status} from where it is now. A report "
                       + "follows its lifecycle forward, may be Closed from any stage before "
                       + "that, and once Closed it stays closed."
            }),
            // Unreachable, and deliberately loud rather than a quiet 500: a new outcome
            // added without a case here is a bug in this switch, not in the caller.
            _ => throw new InvalidOperationException($"Unhandled status outcome '{outcome}'.")
        };
    }

    /// <summary>
    /// Files a report. 201 with the created report — the resource exists and is complete,
    /// so this is an ordinary create, not the 202 that POST /api/workflows returns.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ReportDto>> Create(
        CreateReportDto dto,
        CancellationToken cancellationToken)
    {
        // The reporter is whoever the token says it is. CreateReportDto carries no
        // ReporterId, so there is nothing in the body that could override this.
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(sub, out var reporterId))
        {
            return Unauthorized();
        }

        var created = await _reportService.CreateAsync(dto, reporterId, cancellationToken);

        if (created is null)
        {
            ModelState.AddModelError(nameof(dto.RoomId), $"Room {dto.RoomId} does not exist.");
            return ValidationProblem(ModelState);
        }

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// The clarification questions asked about a report, in the order the form renders
    /// them, each carrying its answer once one has been given.
    ///
    /// A FORM, NOT A TRANSCRIPT. Every item says which bounded control to draw — a yes/no
    /// toggle, a picker over its own Options, or a capped short text box — and there is
    /// nothing in the shape a message thread could be built from. See AnswerType.
    ///
    /// [Authorize] with no policy and no ownership check, matching GET /api/reports/{id}
    /// beside it: a technician and a manager both read a report's clarifications before
    /// acting on the fault, so narrowing the read to the reporter would break both. The
    /// ownership rule belongs on the WRITE, which is where it is.
    ///
    /// An empty list and a missing report are different answers, so the report is looked
    /// up first: a report nobody has clarified yet returns 200 and [], never a 404.
    /// </summary>
    [HttpGet("{id:int}/clarifications")]
    [ProducesResponseType(typeof(IReadOnlyList<ClarificationQuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ClarificationQuestionDto>>> GetClarifications(
        int id,
        CancellationToken cancellationToken)
    {
        if (await _reportService.GetByIdAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        return Ok(await _clarificationService.GetForReportAsync(id, cancellationToken));
    }

    /// <summary>
    /// Submits the reporter's answers to a report's clarification questions.
    ///
    /// THE WHOLE FORM, IN ONE REQUEST, AND THEN THE EXCHANGE IS OVER. There is no reply
    /// field, no thread id and no follow-up round — 204 and done. That is the same
    /// constraint the agent service pins with tests on ClarifierOutput, held on this side
    /// of the wire.
    ///
    /// NONE OF THE CHECKS BEHIND THIS ENDPOINT IS DELEGATED TO THE AGENT. Identity, report
    /// state, question ownership, completeness and whether a chosen option was ever
    /// offered are all deterministic business rules and all of them are C#, in
    /// ClarificationService, in a fixed order — see SubmitAnswersAsync. The agent decides
    /// what to ask and nothing at all about what comes back.
    ///
    /// This action stays thin: it reads the caller's id off the token, makes one service
    /// call, and turns the outcome into a status code.
    /// </summary>
    [HttpPost("{id:int}/clarifications")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitClarifications(
        int id,
        SubmitAnswersRequest request,
        CancellationToken cancellationToken)
    {
        // Who answered is whoever the token says it is. SubmitAnswersRequest carries no
        // user id, so there is nothing in the body that could override this — the same
        // rule as Create above.
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(sub, out var answeredByUserId))
        {
            return Unauthorized();
        }

        var result = await _clarificationService.SubmitAnswersAsync(
            id, answeredByUserId, request, cancellationToken);

        switch (result.Outcome)
        {
            case SubmitAnswersOutcome.Success:
                return NoContent();

            case SubmitAnswersOutcome.ReportNotFound:
                return NotFound();

            // 403, not 404 and not 401: the token is valid and we know exactly who the
            // caller is — we are refusing them, not asking who they are. A body, unlike
            // the policy-generated 403s elsewhere, because this one is an ownership
            // refusal rather than a role refusal and the two read identically without it.
            case SubmitAnswersOutcome.NotTheReporter:
                return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Not your report",
                    Detail = "Clarification questions are answered by the reporter who filed "
                           + "the report, and this report was filed by somebody else."
                });

            case SubmitAnswersOutcome.NotAwaitingClarification:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Report is not awaiting clarification",
                    Detail = $"Report {id} is not waiting on an answer. Either nothing has been "
                           + "asked about it, or it has already been clarified and moved on."
                });

            case SubmitAnswersOutcome.AlreadyAnswered:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Already answered",
                    Detail = $"Question {result.QuestionId} has already been answered. A question "
                           + "takes one answer, not a thread, and the first one stands."
                });

            case SubmitAnswersOutcome.UnknownQuestion:
                return AnswersInvalid(
                    $"Question {result.QuestionId} does not belong to report {id}.");

            case SubmitAnswersOutcome.DuplicateAnswer:
                return AnswersInvalid(
                    $"Question {result.QuestionId} was answered more than once. Each question "
                    + "takes exactly one answer.");

            case SubmitAnswersOutcome.MissingAnswer:
                return AnswersInvalid(
                    $"Question {result.QuestionId} was left unanswered. The whole form is "
                    + "submitted at once, so every question needs an answer.");

            case SubmitAnswersOutcome.OptionNotOffered:
                return AnswersInvalid(
                    $"The answer to question {result.QuestionId} is not one of the options that "
                    + "question offers. A single-select answer must match one of them exactly.");

            default:
                // Unreachable, and deliberately loud rather than a quiet 500: a new outcome
                // added without a case here is a bug in this switch, not in the caller.
                throw new InvalidOperationException(
                    $"Unhandled submit-answers outcome '{result.Outcome}'.");
        }
    }

    /// <summary>
    /// The four content failures are all 400s against the same field, so they share one
    /// shape. ValidationProblem, like everywhere else in this API, rather than a bespoke body.
    /// </summary>
    private ActionResult AnswersInvalid(string detail)
    {
        ModelState.AddModelError(nameof(SubmitAnswersRequest.Answers), detail);
        return ValidationProblem(ModelState);
    }

    /// <summary>
    /// Who is asking, from the token and from nowhere else. Both values are read here once
    /// rather than in each action, so no endpoint can quietly go without one.
    ///
    /// A token this API issued always carries both claims, so a failure here means a token
    /// this API did not issue or one whose shape has changed — 401 either way.
    /// </summary>
    private bool TryGetCaller(out int callerId, out Role callerRole)
    {
        callerRole = default;

        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(sub, out callerId))
        {
            return false;
        }

        // Parsed by NAME, matching how AuthService writes it and how the database stores it.
        // A client never sees, and never needs, an ordinal.
        return Enum.TryParse(User.FindFirst("role")?.Value, out callerRole);
    }
}
