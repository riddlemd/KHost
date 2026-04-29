using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IRepository<T> : ISearchable<T> where T : RepositoryModel
{
    Task<T> CreateAsync(T entity);
    Task<T?> ReadAsync(Guid id);
    Task UpdateAsync(T entity);
    Task<bool> DeleteAsync(Guid id);
    Task<PaginatedResult<T>> ReadAllAsync(int pageNumber = 1, int pageSize = 0);
    Task<PaginatedResult<T>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort);
    Task<bool> HasAnyAsync();
}
