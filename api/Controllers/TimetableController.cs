using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Controllers;

/// <summary>
/// The campus timetable mirror. FacilitiesManager only — the people who schedule work
/// around classes — and, like every one-role policy here, an Admin is refused too.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = nameof(Role.FacilitiesManager))]
public class TimetableController : ControllerBase
{
    private readonly ITimetableSyncService _syncService;

    public TimetableController(ITimetableSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>
    /// Syncs now, rather than waiting for the hourly timer.
    ///
    /// 200 EVEN WHEN GOOGLE IS DOWN. A degraded sync is a successful request whose answer is
    /// "the cache was not refreshed, and here is how old it is" — Degraded, FailureReason,
    /// CacheAgeMinutes and, past 24 hours, a StalenessWarning. The existing classes stay in
    /// place and the slot finder keeps reading them.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(TimetableSyncResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TimetableSyncResultDto>> Sync(CancellationToken cancellationToken)
    {
        return Ok(await _syncService.SyncAsync(cancellationToken));
    }
}
