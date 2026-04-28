using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface ISearchable<T> where T : class
{
    Task<PaginatedResult<T>> SearchAsync<TOptions>(string query, int pageNumber = 1, int pageSize = 50, TOptions? options = null) where TOptions : class;
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50);
    // Sort-aware overload: requires all parameters to be specified
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort);
}
