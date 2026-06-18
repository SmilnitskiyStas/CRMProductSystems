namespace ShelfGuard.Application.Common;

/// <summary>Paginated result envelope returned by all LIST endpoints.</summary>
public sealed class PagedResult<T>
{
    public List<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;
}

/// <summary>Query parameters for paginated LIST endpoints.</summary>
public sealed class PagedQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;

    /// <summary>Page clamped to [1, ∞). PageSize clamped to [1, 200].</summary>
    public int ClampedPage => Math.Max(1, Page);
    public int ClampedPageSize => Math.Clamp(PageSize, 1, 200);
}
