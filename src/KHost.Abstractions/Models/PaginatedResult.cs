namespace KHost.Abstractions.Models;

public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    // Guards the default-constructed PageSize of 0, which would otherwise divide by zero.
    public int TotalPages => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
