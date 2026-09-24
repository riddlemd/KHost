namespace KHost.Abstractions.Models;

/// <summary>One page of a larger result set.</summary>
public class PaginatedResult<T>
{
    /// <summary>The rows on this page.</summary>
    public List<T> Items { get; set; } = [];

    /// <summary>How many rows match in total, across every page.</summary>
    public int TotalCount { get; set; }

    /// <summary>Which page this is, one-based.</summary>
    public int PageNumber { get; set; }

    /// <summary>How many rows a page holds.</summary>
    public int PageSize { get; set; }

    // Guards the default-constructed PageSize of 0, which would otherwise divide by zero.
    /// <summary>How many pages the whole result set spans.</summary>
    public int TotalPages => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

    /// <summary>Whether a page before this one exists.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>Whether a page after this one exists.</summary>
    public bool HasNextPage => PageNumber < TotalPages;
}
