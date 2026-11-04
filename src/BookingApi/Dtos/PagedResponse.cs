namespace BookingApi.Dtos;

/// <summary>
/// One page of results from a list endpoint, plus what a client needs to request the next page.
/// </summary>
/// <typeparam name="T">The type of item on the page.</typeparam>
public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
