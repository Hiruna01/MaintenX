using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IReportService
{
    /// <summary>
    /// The most reports `get_related_open_reports` will return.
    ///
    /// A constant, not a field on <see cref="ToolCallRequest"/>, for the same reason as
    /// IAssetService.MaxToolHistoryRows: the agent names a tool and an id, and how much
    /// one call can pull is not the caller's to widen.
    /// </summary>
    const int MaxToolRelatedReports = 10;

    /// <summary>
    /// One page of reports, filtered and sorted, through the existing PagedResult&lt;T&gt;.
    /// There is no second pagination type in this project.
    ///
    /// WHO MAY SEE WHAT IS DECIDED HERE, NOT BY THE CALLER. A Reporter is scoped to their
    /// own reports; a FacilitiesManager and an Admin see the estate. The scope is applied
    /// to the query as a Where clause before anything is counted or paged, so a Reporter's
    /// TotalCount describes their own reports and nobody else's — a client cannot widen it
    /// by asking for a different page, and there is no filter parameter that turns it off.
    ///
    /// <paramref name="callerId"/> and <paramref name="callerRole"/> are parameters rather
    /// than DTO fields for the same reason CreateAsync takes reporterId: they come from the
    /// caller's signed token, not from anything the caller sent as data.
    ///
    /// <paramref name="dateFrom"/> and <paramref name="dateTo"/> are calendar dates and
    /// BOTH ENDS ARE INCLUSIVE — a report filed at any time on dateTo is in the range. See
    /// the implementation for why they are DateOnly on the way in and DateTime by the time
    /// they reach the query.
    /// </summary>
    Task<PagedResult<ReportListItemDto>> GetAllAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One report with its room, its asset, the clarification questions asked about it with
    /// whatever answers have come back, and the agent steps recorded for it.
    ///
    /// The same visibility rule as <see cref="GetAllAsync"/>, and it has to be: a list that
    /// hides other people's reports while a detail read hands them over by id would be a
    /// rule that only looks enforced. Returns null both when the report does not exist and
    /// when the caller may not see it — telling those apart is the controller's business,
    /// which is why <see cref="ExistsAsync"/> sits beside this one, the same way
    /// AssetsController calls TagExistsAsync before choosing its status code.
    /// </summary>
    Task<ReportDetailDto?> GetDetailAsync(
        int id,
        int callerId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a report exists AT ALL, ignoring who is asking. Used by the controller to
    /// tell a 404 from a 403 after <see cref="GetDetailAsync"/> returns null.
    /// </summary>
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a report to a new status, refusing any move the lifecycle does not allow.
    ///
    /// THE LEGAL MOVES ARE A HARDCODED MAP IN C#, and an illegal one is refused rather than
    /// quietly written — a status that can go anywhere is not a lifecycle, it is a free-text
    /// field with an enum's name. This is a deterministic business rule and PROJECT RULES
    /// puts those in C#; no agent proposes, validates or applies a transition.
    ///
    /// A move to the status the report is already in is refused too. This endpoint asserts
    /// a TRANSITION, and staying put is not one — telling the caller their view is stale is
    /// more use than a 204 that changed nothing.
    /// </summary>
    Task<UpdateReportStatusOutcome> UpdateStatusAsync(
        int id,
        ReportStatus newStatus,
        CancellationToken cancellationToken = default);

    /// <summary>Returns null when no report has that id (a 404 for the caller).</summary>
    Task<ReportDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a report for <paramref name="reporterId"/> and raises the agent workflow
    /// for it. Returns null when the referenced room does not exist (a 400 for the caller).
    ///
    /// <paramref name="reporterId"/> is a parameter rather than a DTO field because it
    /// comes from the caller's token, not from anything the caller sent as data.
    /// </summary>
    Task<ReportDto?> CreateAsync(
        CreateReportDto dto,
        int reporterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports filed against this asset that are NOT yet Closed, newest first and capped
    /// at <see cref="MaxToolRelatedReports"/> — what `get_related_open_reports` returns.
    ///
    /// Open, not all, because the question it answers is "is anyone else already seeing
    /// this": a fault closed last year is history and belongs in the service record, not
    /// in a list of live complaints.
    ///
    /// NULL means no asset has that id; an EMPTY LIST means the asset exists and nothing
    /// is open against it. Those are different facts and the tool reports them
    /// differently — same rule as IAssetService.GetRecentServiceHistoryAsync.
    /// </summary>
    Task<IReadOnlyList<ReportDto>?> GetOpenReportsForAssetAsync(
        int assetId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Why an <see cref="IReportService.UpdateStatusAsync"/> call ended as it did. A plain enum
/// naming three outcomes, not the Result&lt;T&gt; wrapper the conventions rule out — same
/// shape and same reasoning as <see cref="SubmitAnswersOutcome"/>.
/// </summary>
public enum UpdateReportStatusOutcome
{
    /// <summary>Moved. A 204.</summary>
    Success,

    /// <summary>No report has that id. A 404.</summary>
    NotFound,

    /// <summary>
    /// The lifecycle does not allow that move, or the report is already in that status. A
    /// 409: the request is well formed and the value is a real member, so nothing about the
    /// body is wrong — it is the report that is not where the caller thinks it is.
    /// </summary>
    IllegalTransition
}
