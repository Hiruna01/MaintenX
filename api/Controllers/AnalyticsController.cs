using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// Estate-wide numbers. Every figure is counted in C# or SQL by a service; nothing here
/// computes anything, and no agent is called.
///
/// Its own controller now that there are two analytics reads. The verification numbers used
/// to sit on VerificationsController under an absolute route, and moved here — to
/// /api/analytics/verification — when /api/analytics/metrics became the estate-wide read.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    /// <summary>
    /// The two roles that may read the estate-wide metrics, by NAME. A role LIST, not two
    /// policies: stacking [Authorize(Policy = ...)] twice would require BOTH roles at once.
    /// Written out rather than inferred, so there is still no "or more senior" rule anywhere.
    /// </summary>
    private const string MetricsRoles = nameof(Role.FacilitiesManager) + "," + nameof(Role.Admin);

    private readonly IAnalyticsService _analyticsService;
    private readonly IVerificationService _verificationService;
    private readonly IAgentMetricsService _agentMetricsService;

    public AnalyticsController(
        IAnalyticsService analyticsService,
        IVerificationService verificationService,
        IAgentMetricsService agentMetricsService)
    {
        _analyticsService = analyticsService;
        _verificationService = verificationService;
        _agentMetricsService = agentMetricsService;
    }

    /// <summary>
    /// Reopen rate (overall, per category, six-month trend), clarification efficiency and
    /// the repeat-failure list, in one MetricsDto.
    ///
    /// <paramref name="fromDate"/> and <paramref name="toDate"/> are optional UTC calendar
    /// days, both ends inclusive. A range that ends before it starts is a 400 — it would
    /// otherwise come back as a page of zeros that looks like real data.
    ///
    /// FacilitiesManager and Admin. The figures are across the whole estate, and a Reporter
    /// or a Technician sees only their own slice of it everywhere else.
    /// </summary>
    [HttpGet("metrics")]
    [Authorize(Roles = MetricsRoles)]
    [ProducesResponseType(typeof(MetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MetricsDto>> GetMetrics(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        if (fromDate is not null && toDate is not null && fromDate > toDate)
        {
            ModelState.AddModelError(nameof(fromDate), "fromDate must be on or before toDate.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _analyticsService.GetMetricsAsync(fromDate, toDate, cancellationToken));
    }

    /// <summary>
    /// How the agents themselves are doing — runs, failures, retries, latency and the tokens
    /// the provider reported, per agent and per day, with the slowest and costliest runs and
    /// an ESTIMATED cost when a price is configured. See AgentMetricsDto.
    ///
    /// The same optional UTC-day range as the metrics, both ends inclusive, on the step's
    /// CreatedAt; a range that ends before it starts is a 400. FacilitiesManager and Admin,
    /// like the metrics: the figures are about every report's runs.
    /// </summary>
    [HttpGet("agents")]
    [Authorize(Roles = MetricsRoles)]
    [ProducesResponseType(typeof(AgentMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AgentMetricsDto>> GetAgentMetrics(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        if (fromDate is not null && toDate is not null && fromDate > toDate)
        {
            ModelState.AddModelError(nameof(fromDate), "fromDate must be on or before toDate.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _agentMetricsService.GetAgentMetricsAsync(fromDate, toDate, cancellationToken));
    }

    /// <summary>
    /// How the verification loop itself is doing — every check by status, the confirmation
    /// and reopen rates, how long reporters take to answer, and how far the sweep is behind.
    /// See VerificationMetricsDto.
    ///
    /// FacilitiesManager only, as it was on VerificationsController: the policy names one
    /// role, so an Admin is refused.
    /// </summary>
    [HttpGet("verification")]
    [Authorize(Policy = nameof(Role.FacilitiesManager))]
    [ProducesResponseType(typeof(VerificationMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VerificationMetricsDto>> GetVerificationMetrics(CancellationToken cancellationToken)
    {
        return Ok(await _verificationService.GetMetricsAsync(cancellationToken));
    }
}
