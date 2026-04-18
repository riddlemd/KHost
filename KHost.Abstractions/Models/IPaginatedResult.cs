namespace KHost.Abstractions.Models;

public interface IPaginatedResult<T>
{
    List<T> Items { get; set; }
    int TotalCount { get; set; }
    int PageNumber { get; set; }
    int PageSize { get; set; }
    int TotalPages { get; }
    bool HasPreviousPage { get; }
    bool HasNextPage { get; }
}
