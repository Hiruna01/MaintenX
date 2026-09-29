using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// The agent workflows, for the people who oversee them: a FacilitiesManager and an Admin.
///
/// A role LIST on the class, not a policy. A workflow's objective is the reporter's own
/// description and its steps are everything the agents read and said about the report, so
/// the whole-estate list and detail belong to the same two roles as the report intake queue
/// (the web's MANAGER_ROLES). A Reporter follows their own report through GET /api/reports,
/// which is scoped to them; a Technician reads the diagnosis on the work order. Neither can
/// start a run: every run a reporter needs is started for them by filing the report.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = OverseerRoles)]
public class WorkflowsController : ControllerBase
{
    /// <summary>Who may list, read and start workflows, by NAME. See the class summary.</summary>
    private const string OverseerRoles = nameof(Role.FacilitiesManager) + "," + nameof(Role.Admin);

    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowQueue _workflowQueue;
    private readonly IVerificationService _verificationService;

    public WorkflowsController(
        IWorkflowService workflowService,
        IWorkflowQueue workflowQueue,
        IVerificationService verificationService)
    {
        _workflowService = workflowService;
        _workflowQueue = workflowQueue;
        _verificationService = verificationService;
    }

    /// <summary>
    /// Starts a workflow and returns 202 Accepted immediately.
    ///
    /// 202, not 201: the resource exists, but the work it describes has not been done yet.
    /// The agent run takes far longer than an HTTP request should, so this method only
    /// writes the row and queues the id — it never calls the agent service. The Location
    /// header points at the GET the client polls.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowSummaryDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowSummaryDto>> Start(
        StartWorkflowRequest dto,
        CancellationToken cancellationToken)
    {
        var result = await _workflowService.StartAsync(dto, cancellationToken);

        switch (result.Outcome)
        {
            case StartWorkflowOutcome.ReportNotFound:
                ModelState.AddModelError(nameof(dto.ReportId), $"Report {dto.ReportId} does not exist.");
                return ValidationProblem(ModelState);

            case StartWorkflowOutcome.ReportClosed:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Report is closed",
                    Detail = $"Report {dto.ReportId} is closed. A fault that returns is filed as a new report."
                });

            case StartWorkflowOutcome.RunInProgress:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Report already has a live run",
                    Detail = $"Report {dto.ReportId}'s latest workflow has not ended. A new run can start " +
                             "once it is Failed or Closed."
                });
        }

        var created = result.Workflow!;

        // Not cancellationToken: the request's token is cancelled the moment this response
        // is written, which would abort the very hand-off we just promised the client.
        await _workflowQueue.EnqueueAsync(created.Id, CancellationToken.None);

        return AcceptedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// The polling read: the workflow's current state and every AgentStep recorded against
    /// it, oldest first. This is what a client watches while the runner works in the
    /// background — the POST above never waits for it.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WorkflowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var workflow = await _workflowService.GetByIdAsync(id, cancellationToken);
        return workflow is null ? NotFound() : Ok(workflow);
    }

    /// <summary>
    /// Lists workflows, newest first. Optionally filtered by state; always paginated.
    /// <paramref name="state"/> is bound by enum NAME ("AwaitingManagerApproval"), the
    /// same string the JSON contract and the database use.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<WorkflowSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<WorkflowSummaryDto>>> GetAll(
        [FromQuery] WorkflowState? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _workflowService.GetAllAsync(state, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Runs one pass of the verification sweep now, rather than waiting for the timer:
    /// Completed workflows at least Verification:DelayDays old move to AwaitingVerification,
    /// and their reporters are asked.
    ///
    /// Not optional: Render's free tier sleeps an idle service, and a demo cannot wait an
    /// hour for VerificationSweepService to tick. It is the SAME pass the timer runs, under
    /// the same lock, so pressing it while the timer is mid-pass waits rather than doubling
    /// up — and pressing it twice moves nothing twice, because a moved workflow is no longer
    /// Completed.
    ///
    /// FacilitiesManager only, and like every one-role policy here an Admin is refused too.
    /// 200 even when some rows failed — each is counted in Failed, and the rest were processed.
    /// </summary>
    [HttpPost("verification-sweep")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(VerificationSweepResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VerificationSweepResultDto>> RunVerificationSweep(
        CancellationToken cancellationToken)
    {
        return Ok(await _verificationService.ProcessDueChecksAsync(cancellationToken));
    }
}
