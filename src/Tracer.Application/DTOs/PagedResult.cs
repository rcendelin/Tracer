namespace Tracer.Application.DTOs;

/// <summary>
/// Generic paged result wrapper for list endpoints.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>Items in the current page.</summary>
    public required IReadOnlyCollection<T> Items { get; init; }

    /// <summary>Total number of items matching the query, across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Zero-based page index.</summary>
    public required int Page { get; init; }

    /// <summary>Number of items per page (clamped to 1–100 at endpoint level).</summary>
    public required int PageSize { get; init; }

    /// <summary>Total page count derived from <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary><c>true</c> when another page is available after this one.</summary>
    public bool HasNextPage => Page < TotalPages - 1;

    /// <summary><c>true</c> when a previous page exists (i.e. <see cref="Page"/> &gt; 0).</summary>
    public bool HasPreviousPage => Page > 0;
}
