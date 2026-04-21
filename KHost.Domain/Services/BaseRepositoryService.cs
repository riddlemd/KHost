using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public abstract class BaseRepositoryService<TClass, TRepository> : BaseService, IRepositoryService<TClass>
    where TClass : class
    where TRepository : IRepository<TClass>
{
    protected readonly TRepository Repository;

    protected BaseRepositoryService(ILogger logger, TRepository repository)
        : base(logger)
    {
        Repository = repository;
    }

    public virtual async Task<TClass> CreateAsync(TClass entity)
    {
        var savedEntity = await Repository.CreateAsync(entity);

        InvokeStateChanged();

        return savedEntity;
    }

    public virtual async Task<TClass?> ReadAsync(Guid id)
    {
        return await Repository.ReadAsync(id);
    }

    public virtual async Task UpdateAsync(TClass entity)
    {
        await Repository.UpdateAsync(entity);

        InvokeStateChanged();
    }

    public virtual async Task<bool> DeleteAsync(Guid id)
    {
        var success = await Repository.DeleteAsync(id);

        if (success)
            InvokeStateChanged();

        return success;
    }

    public virtual async Task<PaginatedResult<TClass>> ReadAllAsync(int pageNumber = 1, int pageSize = 0)
    {
        return await Repository.ReadAllAsync(pageNumber, pageSize);
    }

    public virtual async Task<PaginatedResult<TClass>> SearchAsync<TOptions>(string query, int pageNumber = 0, int pageSize = 0, TOptions? options = null)
        where TOptions : class
    {
        return await Repository.SearchAsync(query, pageNumber, pageSize, options);
    }

    public virtual async Task<PaginatedResult<TClass>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
    {
        return await Repository.SearchAsync(query, pageNumber, pageSize);
    }
}
