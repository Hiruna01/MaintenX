using System.Diagnostics;
using System.Text.Json;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Middleware;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// The only door the Python agent service has into this system's data.
///
/// The agent holds no database credentials. Everything it can read, it reads through one
/// of the tools named in <see cref="AllowedTools"/> below — a dictionary compiled into
/// this assembly. The tool name arrives from the caller; the set of legal names does not.
///
/// The route is written out in full rather than using [controller], because the URL the
/// agent calls is /api/internal/tools/{toolName} and "internal" is a meaningful segment.
/// </summary>
[ApiController]
[Route("api/internal/tools")]
[ServiceFilter(typeof(AgentSecretFilter))]
public class InternalToolsController : ControllerBase
{
    /// <summary>
    /// Handler for one allow-listed tool. Both registered tools look a single entity up
    /// by id, which is why the argument is just an int — see ToolCallRequest.
    /// </summary>
    private delegate Task<object?> ToolHandler(
        InternalToolsController controller,
        int id,
        CancellationToken cancellationToken);

    /// <summary>
    /// THE ALLOW-LIST. Hardcoded, static, ordinal-compared.
    ///
    /// It is not read from configuration, not read from the request, and not described in
    /// a prompt: the agent may ask for a name, and this dictionary decides whether that
    /// name means anything. A name that is not a key here cannot reach any service, so
    /// the worst a confused or manipulated model can do is earn a 404 and a warning line.
    /// Adding a capability is a code change, a pull request and a review.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ToolHandler> AllowedTools =
        new Dictionary<string, ToolHandler>(StringComparer.Ordinal)
        {
            ["get_room"] = (controller, id, ct) => controller.GetRoomAsync(id, ct),
            ["get_building"] = (controller, id, ct) => controller.GetBuildingAsync(id, ct)
        };

    private readonly IRoomService _roomService;
    private readonly IBuildingService _buildingService;
    private readonly IWorkflowService _workflowService;
    private readonly ILogger<InternalToolsController> _logger;

    public InternalToolsController(
        IRoomService roomService,
        IBuildingService buildingService,
        IWorkflowService workflowService,
        ILogger<InternalToolsController> logger)
    {
        _roomService = roomService;
        _buildingService = buildingService;
        _workflowService = workflowService;
        _logger = logger;
    }

    /// <summary>
    /// Runs one allow-listed tool and records it as an AgentStep on the workflow.
    /// 401 without the shared secret (handled by AgentSecretFilter, before this runs),
    /// 404 for a tool name that is not in the allow-list, 400 for a body that names a
    /// workflow which does not exist.
    /// </summary>
    [HttpPost("{toolName}")]
    [ProducesResponseType(typeof(ToolCallResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ToolCallResponse>> Invoke(
        string toolName,
        ToolCallRequest request,
        CancellationToken cancellationToken)
    {
        // [Required] on the DTO guarantees both of these are set by the time we get here.
        var workflowId = request.WorkflowId!.Value;
        var id = request.Id!.Value;
        var agentName = string.IsNullOrWhiteSpace(request.AgentName) ? "agent" : request.AgentName;
        var toolCallsJson = SerializeToolCall(toolName, id);

        if (!AllowedTools.TryGetValue(toolName, out var handler))
        {
            // Warn, not info: an agent asking for a tool that does not exist is either a
            // bug or a prompt-injection attempt, and both are worth seeing in the log.
            // The name is logged as a parameter, so it lands as structured data rather
            // than being spliced into the message.
            _logger.LogWarning(
                "Agent {AgentName} requested tool {ToolName}, which is not in the allow-list. Rejected.",
                agentName, toolName);

            // Recorded too, so the rejection shows up in the workflow's audit trail and
            // not only in the log sink. Best effort: if the workflow id is bogus as well,
            // there is no parent row to hang it on and the 404 still stands.
            await _workflowService.RecordStepAsync(
                workflowId,
                agentName,
                toolCallsJson,
                payloadJson: null,
                durationMs: 0,
                validationResult: "RejectedUnknownTool",
                errorMessage: $"Tool '{toolName}' is not in the allow-list.",
                cancellationToken);

            return NotFound();
        }

        if (!await _workflowService.ExistsAsync(workflowId, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.WorkflowId), $"Workflow {workflowId} does not exist.");
            return ValidationProblem(ModelState);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await handler(this, id, cancellationToken);
        stopwatch.Stop();

        var response = new ToolCallResponse(toolName, result is not null, result);

        await _workflowService.RecordStepAsync(
            workflowId,
            agentName,
            toolCallsJson,
            payloadJson: JsonSerializer.Serialize(response),
            durationMs: (int)stopwatch.ElapsedMilliseconds,
            validationResult: result is not null ? "Ok" : "NotFound",
            errorMessage: null,
            cancellationToken);

        return Ok(response);
    }

    // ---------------------------------------------------------------------------
    // Tool handlers. Each one is a thin delegation to the service that already owns
    // that data — no new query paths, so a tool can never read more than the matching
    // REST endpoint would.
    // ---------------------------------------------------------------------------

    private async Task<object?> GetRoomAsync(int id, CancellationToken cancellationToken) =>
        await _roomService.GetByIdAsync(id, cancellationToken);

    private async Task<object?> GetBuildingAsync(int id, CancellationToken cancellationToken) =>
        await _buildingService.GetByIdAsync(id, cancellationToken);

    private static string SerializeToolCall(string toolName, int id) =>
        // An array because a step may eventually carry several calls; today it is one.
        JsonSerializer.Serialize(new[]
        {
            new { tool = toolName, arguments = new { id } }
        });
}
