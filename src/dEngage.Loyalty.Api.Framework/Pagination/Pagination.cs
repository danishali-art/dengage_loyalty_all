namespace dEngage.Loyalty.Api.Framework.Pagination;

public sealed class PageRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public static PageRequest From(int? page, int? pageSize) => new()
    {
        Page = page is > 0 ? page.Value : 1,
        PageSize = pageSize switch { null or <= 0 => 25, > 100 => 100, _ => pageSize.Value }
    };
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Data { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }
}

public sealed class CursorPage<T>
{
    public required IReadOnlyList<T> Data { get; init; }
    public string? NextCursor { get; init; }

    // Optional, additive: the number of rows matching the request's filters (all pages), for a
    // "1–25 of 120" paginator. Null where an endpoint doesn't count (omitted from the JSON).
    public int? Total { get; init; }
}
