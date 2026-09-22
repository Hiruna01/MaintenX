using CampusFacilities.Api.Dtos;

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
