using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IReportService
{
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
}
