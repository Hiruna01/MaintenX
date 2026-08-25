using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowQueue _workflowQueue;

    public WorkflowsController(IWorkflowService workflowService, IWorkflowQueue workflowQueue)
    {
        _workflowService = workflowService;
        _workflowQueue = workflowQueue;
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
    public async Task<ActionResult<WorkflowSummaryDto>> Start(
        StartWorkflowRequest dto,
        CancellationToken cancellationToken)
    {
        var created = await _workflowService.StartAsync(dto, cancellationToken);

        // Not cancellationToken: the request's token is cancelled the moment this response
        // is written, which would abort the very hand-off we just promised the client.
        await _workflowQueue.EnqueueAsync(created.Id, CancellationToken.None);

        return AcceptedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

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
}
