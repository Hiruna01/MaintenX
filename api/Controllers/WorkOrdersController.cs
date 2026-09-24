using System.ComponentModel.DataAnnotations;
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
    /// One page of work orders. <paramref name="search"/> matches the asset tag or the
    /// report's description; every other filter is exact, and they all combine;
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
        [FromQuery] string? search,
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
            callerId, callerRole, search, status, technicianId, assetId, dateFrom, dateTo,
            sort, page, pageSize, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// The approval queue: every order AwaitingApproval, oldest first, each carrying what a
    /// manager needs to decide it without opening anything else — the approval basis, the
    /// asset's service history and failure summary, and the agent's diagnosis and proposal.
    /// FacilitiesManager only, the same as approve / reject / request-revision: an Admin is
    /// refused here too, and a Technician never sees it.
    ///
    /// Paged through the existing PagedResult&lt;T&gt;. An empty queue is a 200 with no items.
    /// </summary>
    [HttpGet("approvals")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(PagedResult<ApprovalCaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ApprovalCaseDto>>> GetApprovalQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _workOrderService.GetApprovalQueueAsync(page, pageSize, cancellationToken);
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

    /// <summary>
    /// Free blocks of time for work on an asset, earliest first, at most twenty.
    /// FacilitiesManager only — whoever books the work.
    ///
    /// <paramref name="fromDate"/> and <paramref name="toDate"/> are CAMPUS-LOCAL calendar
    /// dates, both inclusive; the slots come back in UTC, ready to post to
    /// POST {id}/schedule unchanged. <paramref name="technicianId"/> is optional: without it
    /// only the room is checked, which is what a manager wants before choosing who to send.
    ///
    /// The rules — working hours, the class buffer, the overlap test — are SlotRules, in C#.
    /// An empty list is a 200: "nothing free in that range" is an answer, not an error.
    /// </summary>
    [HttpGet("slots/available")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(IReadOnlyList<AvailableSlotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AvailableSlotDto>>> GetAvailableSlots(
        [FromQuery, Required, Range(1, int.MaxValue)] int? assetId,
        [FromQuery, Range(1, int.MaxValue)] int? technicianId,
        [FromQuery, Required, Range(15, 1440)] int? durationMinutes,
        [FromQuery, Required] DateOnly? fromDate,
        [FromQuery, Required] DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var result = await _workOrderService.GetAvailableSlotsAsync(
            assetId!.Value, technicianId, durationMinutes!.Value, fromDate!.Value, toDate!.Value,
            cancellationToken);

        switch (result.Outcome)
        {
            case AvailableSlotsOutcome.Success:
                return Ok(result.Slots);

            case AvailableSlotsOutcome.AssetNotFound:
                return QueryInvalid(nameof(assetId), $"Asset {assetId} does not exist.");

            case AvailableSlotsOutcome.NotATechnician:
                return QueryInvalid(nameof(technicianId), "That user does not exist or is not a Technician.");

            case AvailableSlotsOutcome.InvalidDateRange:
                return QueryInvalid(nameof(toDate),
                    "toDate must be on or after fromDate, and the range may cover at most 31 days.");

            case AvailableSlotsOutcome.DurationTooLong:
                return QueryInvalid(nameof(durationMinutes), "The job is longer than the working day.");

            default:
                throw new InvalidOperationException($"Unhandled available-slots outcome '{result.Outcome}'.");
        }
    }

    /// <summary>
    /// Books one visit. FacilitiesManager only. 201 with the new slot, via CreatedAtAction on
    /// the order — which is where its slots are read back.
    ///
    /// AN OFFERED SLOT IS RE-CHECKED HERE, NEVER TRUSTED. Taken since it was offered is a
    /// 409; a slot that could never have been offered (outside the working day, already
    /// started, no UTC offset) is a 400. The order must be assigned first.
    /// </summary>
    [HttpPost("{id:int}/schedule")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(ScheduledSlotDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScheduledSlotDto>> Schedule(
        int id,
        ScheduleWorkOrderDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _workOrderService.ScheduleAsync(id, dto, cancellationToken);

        switch (result.Outcome)
        {
            case ScheduleOutcome.Success:
                return CreatedAtAction(nameof(GetById), new { id }, result.Slot);

            case ScheduleOutcome.NotFound:
                return NotFound();

            case ScheduleOutcome.InvalidState:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Work order is not in a state that allows this",
                    Detail = $"Work order {id} cannot be scheduled from where it is now. Only an "
                           + "order that has cleared approval and is not yet finished can be booked."
                });

            case ScheduleOutcome.NoTechnicianAssigned:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "No technician assigned",
                    Detail = $"Work order {id} has nobody assigned to it. Assign a technician "
                           + "before booking a visit — a booking is time in somebody's diary."
                });

            case ScheduleOutcome.NotBookable:
                ModelState.AddModelError(nameof(dto.StartsAt),
                    "That slot is not bookable: it must be inside the working day (Monday to "
                    + "Friday), not already started, end after it starts, and be sent in UTC "
                    + "with its offset, as GET slots/available returns it.");
                return ValidationProblem(ModelState);

            case ScheduleOutcome.SlotTaken:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Slot no longer available",
                    Detail = "That time now overlaps a class in the room or another visit for "
                           + "the technician. Ask for available slots again."
                });

            default:
                throw new InvalidOperationException($"Unhandled schedule outcome '{result.Outcome}'.");
        }
    }

    /// <summary>A 400 against one query parameter, in the same ValidationProblem shape as a body.</summary>
    private ActionResult QueryInvalid(string parameter, string detail)
    {
        ModelState.AddModelError(parameter, detail);
        return ValidationProblem(ModelState);
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
