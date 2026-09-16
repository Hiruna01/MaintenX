using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowQueue _workflowQueue;

    public ReportService(
        AppDbContext db,
        IWorkflowService workflowService,
        IWorkflowQueue workflowQueue)
    {
        _db = db;
        _workflowService = workflowService;
        _workflowQueue = workflowQueue;
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

    private static ReportDto ToDto(Report r) =>
        new(r.Id, r.ReporterId, r.RoomId, r.AssetId, r.Description, r.Status, r.PhotoUrl,
            r.CreatedAt, r.UpdatedAt);
}
