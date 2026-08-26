namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One page of a list endpoint. Not a Result&lt;T&gt; wrapper — it carries no success or
/// error information, only the paging counters a client needs to render "page 2 of 7".
/// </summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
