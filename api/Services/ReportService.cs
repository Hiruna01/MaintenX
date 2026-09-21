using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class ReportService : IReportService
{
    /// <summary>Largest page a client may ask for, so one request cannot pull the table.</summary>
    private const int MaxPageSize = 100;

    /// <summary>
    /// THE REPORT LIFECYCLE, as a map of where each status may go next. Hardcoded in C#,
    /// like InternalToolsController's tool allow-list and for the same instinct: what the
    /// system permits must not be describable by anything outside it.
    ///
    /// The shape is a funnel with one escape hatch. A fault moves forward through
    /// clarification, diagnosis and a work order, and from ANY of those it may be Closed —
    /// because a fault can turn out to be nothing, be fixed by somebody in passing, or be
    /// reported twice, and none of those should have to be walked through diagnosis to be
    /// filed away.
    ///
    /// Submitted may jump straight to Diagnosed: clarification is what the agent asks for
    /// when it needs more detail, and a report that arrives clear enough to act on does not
    /// need it.
    ///
    /// CLOSED IS TERMINAL and there is deliberately no way back. A fault that returns is a
    /// new report with its own history, not this one reopened — the same rule the
    /// verification loop follows when a failed repair produces a NEW work order rather than
    /// reusing the row. Reopening would overwrite the record that this one was resolved.
    /// Note that ReportStatus has no Reopened member, unlike WorkflowState, which is the
    /// same decision stated in the enum.
    /// </summary>
    private static readonly IReadOnlyDictionary<ReportStatus, ReportStatus[]> LegalTransitions =
        new Dictionary<ReportStatus, ReportStatus[]>
        {
            [ReportStatus.Submitted] = new[]
            {
                ReportStatus.AwaitingClarification, ReportStatus.Diagnosed, ReportStatus.Closed
            },
            [ReportStatus.AwaitingClarification] = new[]
            {
                ReportStatus.Clarified, ReportStatus.Closed
            },
            [ReportStatus.Clarified] = new[]
            {
                ReportStatus.Diagnosed, ReportStatus.Closed
            },
            [ReportStatus.Diagnosed] = new[]
            {
                ReportStatus.WorkOrderRaised, ReportStatus.Closed
            },
            [ReportStatus.WorkOrderRaised] = new[]
            {
                ReportStatus.Closed
            },
            [ReportStatus.Closed] = Array.Empty<ReportStatus>()
        };

    private readonly AppDbContext _db;
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowQueue _workflowQueue;
    private readonly IClarificationService _clarificationService;

    public ReportService(
        AppDbContext db,
        IWorkflowService workflowService,
        IWorkflowQueue workflowQueue,
        IClarificationService clarificationService)
    {
        _db = db;
        _workflowService = workflowService;
        _workflowQueue = workflowQueue;
        _clarificationService = clarificationService;
    }

    /// <summary>
    /// THE VISIBILITY RULE, in one place so it cannot be applied to the list and forgotten
    /// on the detail read. A FacilitiesManager and an Admin see every report; everybody
    /// else sees their own.
    ///
    /// Written as "who sees everything" rather than "who is restricted", so it FAILS
    /// CLOSED: a role added to the enum later is scoped to its own reports until somebody
    /// deliberately widens it, rather than being handed the estate by an else branch that
    /// was never revisited. A Technician is scoped today for exactly that reason — work
    /// reaches them through a WorkOrder, not by browsing reports.
    /// </summary>
    private static bool SeesEveryReport(Role role) =>
        role is Role.FacilitiesManager or Role.Admin;

    public async Task<PagedResult<ReportListItemDto>> GetAllAsync(
        int callerId,
        Role callerRole,
        string? search = null,
        ReportStatus? status = null,
        int? roomId = null,
        int? assetId = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        ReportSort sort = ReportSort.CreatedAt,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Clamp rather than reject, the same as AssetService and WorkflowService: a client
        // asking for page 0 gets page 1, not a 400.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);

        var query = _db.Reports.AsNoTracking();

        // THE VISIBILITY SCOPE GOES ON FIRST, before the filters and before the count, so
        // there is no ordering of the clauses below that could let a row out. A Reporter's
        // TotalCount is their own reports; a page number cannot reach past it.
        if (!SeesEveryReport(callerRole))
        {
            query = query.Where(r => r.ReporterId == callerId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLower().Contains(), never EF.Functions.ILike: ILike is Npgsql-only and the
            // tests run on SQLite by default, so it would be a runtime failure nothing on a
            // developer's machine would catch. Lowering both sides is also what makes the
            // two providers agree — SQLite's LIKE is already case-insensitive, PostgreSQL's
            // is not. Same reasoning as the asset search.
            //
            // The description only. A reporter searches for the words they typed, and there
            // is no second searchable field on a report the way an asset has its tag.
            var term = search.Trim().ToLower();

            query = query.Where(r => r.Description.ToLower().Contains(term));
        }

        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        if (roomId is not null)
        {
            query = query.Where(r => r.RoomId == roomId);
        }

        if (assetId is not null)
        {
            query = query.Where(r => r.AssetId == assetId);
        }

        // The dates arrive as calendar dates and CreatedAt is a timestamp, so the bounds are
        // worked out HERE, in C#, and only DateTime values reach the query. DateOnly
        // arithmetic does not translate to SQLite, where dates are TEXT — the same reason
        // the failure summary is computed in memory.
        //
        // UTC to match CreatedAt, which AppDbContext stamps with DateTime.UtcNow.
        if (dateFrom is not null)
        {
            var from = dateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAt >= from);
        }

        if (dateTo is not null)
        {
            // The day AFTER dateTo, exclusive — which is what makes dateTo inclusive. A
            // "<= dateTo at midnight" would silently drop everything filed during the last
            // day of the range, and a client asking for today would get nothing at all.
            var toExclusive = dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAt < toExclusive);
        }

        // Counted after every filter but before paging, so TotalCount describes the whole
        // filtered set rather than the slice being returned.
        var totalCount = await query.CountAsync(cancellationToken);

        // Id breaks every tie. Without a total order two reports sharing a timestamp — and
        // they do, since CreatedAt is stamped per SaveChanges and a seed writes many at once
        // — could order differently between two queries, and a row would appear on both
        // page 1 and page 2, or on neither.
        //
        // A NOTE ON SORTING BY STATUS: the column is the enum's NAME, so this orders
        // alphabetically (AwaitingClarification, Clarified, Closed, ...), not by lifecycle
        // position. That is a consequence of storing enums as strings, which is worth far
        // more than a tidy ORDER BY — and grouping is what a caller sorting by status
        // actually wants. Ordering by the lifecycle would mean a CASE expression here or an
        // ordinal in the database, and the ordinal is exactly what this project refuses.
        query = sort switch
        {
            ReportSort.Status => query.OrderBy(r => r.Status).ThenByDescending(r => r.Id),
            _ => query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReportListItemDto(
                r.Id,
                r.ReporterId,
                r.RoomId,
                // Denormalised onto the row by the query rather than fetched per item: a
                // list shows the room's name and nothing else about it, and a RoomDto per
                // row would be a join's worth of data nothing renders.
                r.Room!.Name,
                r.AssetId,
                r.Description,
                r.Status,
                // The one thing a list needs to say about clarification — "this report is
                // waiting on you" — without pulling the questions themselves.
                r.ClarificationQuestions.Count(q => q.Answer == null),
                r.CreatedAt,
                r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReportListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<ReportDetailDto?> GetDetailAsync(
        int id,
        int callerId,
        Role callerRole,
        CancellationToken cancellationToken = default)
    {
        var report = await _db.Reports
            .AsNoTracking()
            .Include(r => r.Room)
            .Include(r => r.Asset)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (report is null)
        {
            return null;
        }

        // THE SAME RULE AS THE LIST, applied here rather than assumed to have been applied
        // there. A list that hides other people's reports while this hands one over by id
        // would be a rule that only looks enforced.
        if (!SeesEveryReport(callerRole) && report.ReporterId != callerId)
        {
            return null;
        }

        // Through the service that owns them, not a query written here, so there is exactly
        // one place that knows how a stored OptionsJson becomes a list of choices. Same
        // instinct as the tool handlers delegating to the service that owns the data.
        var questions = await _clarificationService.GetForReportAsync(id, cancellationToken);

        // Every step of every workflow raised for this report, oldest first. Id is monotonic
        // per insert, so it orders identically to CreatedAt but without ties inside the same
        // millisecond — the same ordering WorkflowService uses for a workflow's own steps.
        var steps = await _db.AgentSteps
            .AsNoTracking()
            .Where(s => s.Workflow!.ReportId == id)
            .OrderBy(s => s.Id)
            .Select(s => new AgentStepDto(
                s.Id, s.WorkflowId, s.AgentName, s.ToolCallsJson, s.DurationMs,
                s.ValidationResult, s.ErrorMessage, s.PayloadJson, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new ReportDetailDto(
            report.Id,
            report.ReporterId,
            new RoomDto(
                report.Room!.Id, report.Room.BuildingId, report.Room.Name, report.Room.Code,
                report.Room.Floor, report.Room.CreatedAt, report.Room.UpdatedAt),
            // Null when the report names no asset, which is the normal case rather than the
            // exception — see Report.AssetId.
            report.Asset is null ? null : ToAssetDto(report.Asset),
            report.Description,
            report.Status,
            report.PhotoUrl,
            report.CreatedAt,
            report.UpdatedAt,
            questions,
            steps);
    }

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Reports.AnyAsync(r => r.Id == id, cancellationToken);

    public async Task<UpdateReportStatusOutcome> UpdateStatusAsync(
        int id,
        ReportStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (report is null)
        {
            return UpdateReportStatusOutcome.NotFound;
        }

        // THE TRANSITION CHECK, AND IT REFUSES RATHER THAN SHRUGS. Reading the map is the
        // whole rule: if the status the report is in does not list the one being asked for,
        // the move does not happen and the caller is told so. Silently allowing it would
        // make every other status in the lifecycle advisory.
        //
        // This also covers a move to the status the report is already in: no status lists
        // itself, so that is refused by the same line. The endpoint asserts a transition,
        // and staying put is not one.
        //
        // No agent proposes or approves any of this. It is a deterministic business rule
        // over two values, and PROJECT RULES keeps those in C#.
        if (!LegalTransitions[report.Status].Contains(newStatus))
        {
            return UpdateReportStatusOutcome.IllegalTransition;
        }

        report.Status = newStatus;

        await _db.SaveChangesAsync(cancellationToken);

        return UpdateReportStatusOutcome.Success;
    }

    public async Task<ReportDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var report = await _db.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return report is null ? null : ToDto(report);
    }

    public async Task<ReportDto?> CreateAsync(
        CreateReportDto dto,
        int reporterId,
        CancellationToken cancellationToken = default)
    {
        // Checked here rather than left to the foreign key, so a bad room id is a 400
        // with a named field instead of a 500 out of the database driver.
        var roomExists = await _db.Rooms.AnyAsync(r => r.Id == dto.RoomId, cancellationToken);

        if (!roomExists)
        {
            return null;
        }

        // ReporterId is not checked the same way: it comes from a signed, unexpired token
        // this API issued, and there is no endpoint that deletes a user, so a token whose
        // subject has vanished cannot arise. If user deletion is ever added, this needs
        // the same guard as the room above.
        var report = new Report
        {
            ReporterId = reporterId,
            RoomId = dto.RoomId,
            Description = dto.Description,
            Status = ReportStatus.Submitted
        };

        _db.Reports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);

        // Raising the workflow is part of filing a report, so it lives here rather than in
        // the controller: the controller's job is one call and a status code.
        //
        // This does NOT call the agent service. StartAsync only writes the row, and the
        // queue hand-off is what keeps POST /api/reports fast — the background runner does
        // the slow work. That is why this endpoint can honestly return 201 rather than the
        // 202 POST /api/workflows returns: the report is complete when we answer.
        //
        // The description becomes the objective verbatim. Both are capped at 1000
        // characters, so this cannot overflow.
        var workflow = await _workflowService.StartAsync(
            new StartWorkflowRequest(report.Description, report.Id), cancellationToken);

        // StartAsync only returns null for a ReportId that does not exist, and we just
        // wrote this one in the same DbContext. Defensive, not expected.
        if (workflow is not null)
        {
            // CancellationToken.None, not the request's: that token is cancelled as soon as
            // the response is written, which would abort the hand-off we just promised.
            await _workflowQueue.EnqueueAsync(workflow.Id, CancellationToken.None);
        }

        return ToDto(report);
    }

    public async Task<IReadOnlyList<ReportDto>?> GetOpenReportsForAssetAsync(
        int assetId,
        CancellationToken cancellationToken = default)
    {
        // Asked separately so "no such asset" and "nothing open against it" stay distinct
        // — see the interface. Null here becomes found=false in the tool response.
        if (!await _db.Assets.AnyAsync(a => a.Id == assetId, cancellationToken))
        {
            return null;
        }

        return await _db.Reports
            .AsNoTracking()
            .Where(r => r.AssetId == assetId && r.Status != ReportStatus.Closed)
            // Newest first, and capped: the cap is what makes the order matter, because
            // taking ten of an oldest-first list would hide whatever was reported today.
            .OrderByDescending(r => r.Id)
            .Take(IReportService.MaxToolRelatedReports)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Maps the asset a report blames. Written here rather than reached for across services
    /// because AssetService's own mapper is private to it, and a report's detail read must
    /// not start depending on the registry's internals to render one nested object.
    /// </summary>
    private static AssetDto ToAssetDto(Asset a) =>
        new(a.Id, a.AssetTag, a.Name, a.AssetCategoryId, a.RoomId, a.Manufacturer, a.Model,
            a.InstalledOn, a.WarrantyExpiresOn, a.Status, a.CreatedAt, a.UpdatedAt);

    private static ReportDto ToDto(Report r) =>
        new(r.Id, r.ReporterId, r.RoomId, r.AssetId, r.Description, r.Status, r.PhotoUrl,
            r.CreatedAt, r.UpdatedAt);
}
