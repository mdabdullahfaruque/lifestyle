namespace Lifestyle.SharedKernel.Paging;

/// <summary>Page-based paging. Used by admin tables, where a total and page numbers are wanted.</summary>
public sealed record PageRequest
{
    private const int MaxPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public int Normalised => Page < 1 ? 1 : Page;
    public int Size => PageSize is < 1 or > MaxPageSize ? 20 : PageSize;
    public int Skip => (Normalised - 1) * Size;
}

/// <summary>Wire shape for a page-based collection: <c>{ "items": [...], "page": {...} }</c>.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, PageMeta Page)
{
    public static PagedResult<T> From(IReadOnlyList<T> items, PageRequest request, int totalCount) =>
        new(items, new PageMeta(
            request.Normalised,
            request.Size,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)request.Size)));
}

public sealed record PageMeta(int Number, int Size, int TotalCount, int TotalPages)
{
    public bool HasPrevious => Number > 1;
    public bool HasNext => Number < TotalPages;
}

/// <summary>
/// Cursor paging for buyer-facing feeds and infinite scroll, where OFFSET degrades and items
/// shift under the reader.
/// </summary>
public sealed record CursorRequest(string? Cursor = null, int Limit = 24)
{
    public int Size => Limit is < 1 or > 100 ? 24 : Limit;
}

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public bool HasMore => NextCursor is not null;
}
