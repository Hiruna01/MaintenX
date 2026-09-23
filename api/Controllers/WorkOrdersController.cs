using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Work orders — raising, assigning, approving and completing live maintenance work.
///
/// [Authorize] with no policy on the controller, and a policy per write action. The reads
/// are open to any signed-in user because WHO SEES WHICH ORDERS is a rule in
/// WorkOrderService, not a role check here: a Technician reads their own queue, a manager
/// reads the estate. Raising, assigning and deciding are FacilitiesManager actions;
/// completing is a Technician's. That split keeps 401 and 403 separately demonstrable —
/// no token is 401 everywhere, a Technician's token is 200 on a read and 403 on approve.
///
/// THIS CONTROLLER DECIDES NOTHING. The approval gate, the legal status moves, the
/// transaction on completion and the visibility scope are all C# in WorkOrderService;
/// every action here reads the caller off the token, makes one service call and turns the
/// outcome into a status code.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkOrdersController : ControllerBase
{
    private readonly IWorkOrderService _workOrderService;

    public WorkOrdersController(IWorkOrderService workOrderService)
    {
        _workOrderService = workOrderService;
    }

    /// <summary>
    /// One page of work orders. Every filter is exact and they all combine;
    /// <paramref name="dateFrom"/> and <paramref name="dateTo"/> are calendar dates against
    /// CreatedAt and BOTH ENDS ARE INCLUSIVE. <paramref name="status"/> and
    /// <paramref name="sort"/> bind by enum NAME, so an unknown value is a 400.
    ///
    /// WHO SEES WHAT IS NOT A QUERY PARAMETER. A Technician gets the orders assigned to them
    /// whatever <paramref name="technicianId"/> says; a FacilitiesManager or Admin gets the
    /// estate. Decided in WorkOrderService from the token's role.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<WorkOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<WorkOrderDto>>> GetAll(
        [FromQuery] WorkOrderStatus? status,
        [FromQuery] int? technicianId,
        [FromQuery] int? assetId,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] WorkOrderSort sort = WorkOrderSort.CreatedAt,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCaller(out var callerId, out var callerRole))
        {
            return Unauthorized();
        }

        var result = await _workOrderService.GetAllAsync(
            callerId, callerRole, status, technicianId, assetId, dateFrom, dateTo,
            sort, page, pageSize, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// One work order in full. Scoped exactly like the list: an order that exists but is
    /// assigned to somebody else is a 403, not a 404 — the same as GET /api/reports/{id},
    /// and told apart from a genuine 404 by ExistsAsync.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WorkOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkOrderDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out var callerRole))
        {
            return Unauthorized();
        }

        var order = await _workOrderService.GetDetailAsync(id, callerId, callerRole, cancellationToken);

        if (order is not null)
        {
            return Ok(order);
        }

        return await _workOrderService.ExistsAsync(id, cancellationToken)
            ? NotYours("Technicians see the work orders assigned to them. This one is assigned to somebody else.")
            : NotFound();
    }

    /// <summary>
    /// Raises a work order from a report. FacilitiesManager only. 201 with the created order,
    /// whose Status already says which side of the approval gate it landed on —
    /// AwaitingApproval or Approved. There is no Status on the request body to override it.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(WorkOrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkOrderDto>> Create(
        CreateWorkOrderDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _workOrderService.CreateAsync(dto, cancellationToken);

        switch (result.Outcome)
        {
            case CreateWorkOrderOutcome.Success:
                return CreatedAtAction(nameof(GetById), new { id = result.WorkOrder!.Id }, result.WorkOrder);

            case CreateWorkOrderOutcome.ReportNotFound:
                ModelState.AddModelError(nameof(dto.ReportId), $"Report {dto.ReportId} does not exist.");
                return ValidationProblem(ModelState);

            case CreateWorkOrderOutcome.AssetNotFound:
                ModelState.AddModelError(nameof(dto.AssetId), $"Asset {dto.AssetId} does not exist.");
                return ValidationProblem(ModelState);

            case CreateWorkOrderOutcome.ReportClosed:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Report is closed",
                    Detail = $"Report {dto.ReportId} is Closed, and a closed report stays closed. "
                           + "A fault that has come back is a new report."
                });

            default:
                // Unreachable, and deliberately loud rather than a quiet 500: a new outcome
                // added without a case here is a bug in this switch, not in the caller.
                throw new InvalidOperationException($"Unhandled create outcome '{result.Outcome}'.");
        }
    }

    /// <summary>
    /// Hands the order to a technician. FacilitiesManager only, 204. PUT because it sets the
    /// assignment outright — assigning the same technician twice is the same request twice.
    /// </summary>
    [HttpPut("{id:int}/assign")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(
        int id,
        AssignTechnicianDto dto,
        CancellationToken cancellationToken)
    {
        var outcome = await _workOrderService.AssignAsync(id, dto.TechnicianId, cancellationToken);

        return ToActionResult(id, outcome, nameof(dto.TechnicianId),
            "Only an order that has cleared approval and is not yet finished can be assigned.");
    }

    /// <summary>
    /// Finishes the work. Technician policy, and then the ASSIGNED technician only — the
    /// ownership check is in the service, against the token's id. 204.
    ///
    /// One request writes four things in one transaction: the order Completed, a
    /// ServiceRecord on the asset, a VerificationCheck, and the workflow to
    /// AwaitingVerification. See WorkOrderService.CompleteAsync.
    /// </summary>
    [HttpPost("{id:int}/complete")]
    [Authorize(Policy = nameof(Role.Technician))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(
        int id,
        CompleteWorkOrderDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out _))
        {
            return Unauthorized();
        }

        var outcome = await _workOrderService.CompleteAsync(id, callerId, dto, cancellationToken);

        return ToActionResult(id, outcome, field: null,
            "Only an order that has cleared approval and is not yet finished can be completed.");
    }

    /// <summary>
    /// A manager's yes on an order waiting for one. FacilitiesManager only, 204; anything
    /// not AwaitingApproval is a 409. Who approved is taken from the token.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out _))
        {
            return Unauthorized();
        }

        var outcome = await _workOrderService.ApproveAsync(id, callerId, cancellationToken);

        return ToActionResult(id, outcome, field: null, AwaitingApprovalOnly);
    }

    /// <summary>
    /// A manager's no. FacilitiesManager only, 204. The reason is required — a missing or
    /// blank one is a 400 from model binding, before the order is even looked up.
    /// </summary>
    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        int id,
        RejectWorkOrderDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out _))
        {
            return Unauthorized();
        }

        var outcome = await _workOrderService.RejectAsync(id, callerId, dto.Reason, cancellationToken);

        return ToActionResult(id, outcome, field: null, AwaitingApprovalOnly);
    }

    /// <summary>
    /// Sends the order back to be re-planned with the manager's note. FacilitiesManager only,
    /// 204. The workflow is re-queued in the background; this request does not wait for it.
    /// </summary>
    [HttpPost("{id:int}/request-revision")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestRevision(
        int id,
        RequestRevisionDto dto,
        CancellationToken cancellationToken)
    {
        var outcome = await _workOrderService.RequestRevisionAsync(id, dto.Note, cancellationToken);

        return ToActionResult(id, outcome, field: null, AwaitingApprovalOnly);
    }

    private const string AwaitingApprovalOnly =
        "Only an order that is AwaitingApproval can be approved, rejected or sent back for revision.";

    /// <summary>
    /// The one mapping from an action outcome to a status code, shared by all five actions
    /// so the same failure is the same code everywhere. <paramref name="field"/> names the
    /// request field a NotATechnician 400 is reported against.
    /// </summary>
    private IActionResult ToActionResult(
        int id,
        WorkOrderActionOutcome outcome,
        string? field,
        string invalidStateDetail)
    {
        switch (outcome)
        {
            case WorkOrderActionOutcome.Success:
                return NoContent();

            case WorkOrderActionOutcome.NotFound:
                return NotFound();

            case WorkOrderActionOutcome.NotAssignedToCaller:
                return NotYours("A work order is completed by the technician it is assigned to, "
                              + "and this one is not assigned to you.");

            case WorkOrderActionOutcome.InvalidState:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Work order is not in a state that allows this",
                    Detail = $"Work order {id} cannot do that from where it is now. {invalidStateDetail}"
                });

            case WorkOrderActionOutcome.NotATechnician:
                ModelState.AddModelError(field ?? string.Empty, "That user does not exist or is not a Technician.");
                return ValidationProblem(ModelState);

            case WorkOrderActionOutcome.NoWorkflow:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "No workflow to revise",
                    Detail = $"Work order {id}'s report has no agent workflow, so there is no "
                           + "planning run to send it back to. Reject it instead."
                });

            default:
                // Unreachable, and deliberately loud rather than a quiet 500.
                throw new InvalidOperationException($"Unhandled work order outcome '{outcome}'.");
        }
    }

    /// <summary>
    /// An ownership refusal, with a body — unlike the policy-generated 403s, which are role
    /// refusals. The two read identically without one.
    /// </summary>
    private ObjectResult NotYours(string detail) =>
        StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Not your work order",
            Detail = detail
        });

    /// <summary>
    /// Who is asking, from the token and from nowhere else — the same helper as
    /// ReportsController. A token this API issued always carries both claims, so a failure
    /// here is a 401.
    /// </summary>
    private bool TryGetCaller(out int callerId, out Role callerRole)
    {
        callerRole = default;

        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(sub, out callerId))
        {
            return false;
        }

        return Enum.TryParse(User.FindFirst("role")?.Value, out callerRole);
    }
}
