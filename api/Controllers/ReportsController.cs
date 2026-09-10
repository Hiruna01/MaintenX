using CampusFacilities.Api.Dtos;
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

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var report = await _reportService.GetByIdAsync(id, cancellationToken);
        return report is null ? NotFound() : Ok(report);
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
}
