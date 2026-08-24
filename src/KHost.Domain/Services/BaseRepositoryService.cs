using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public abstract class BaseRepositoryService<TClass, TRepository> : BaseService, IRepositoryService<TClass>
    where TClass : RepositoryModel
    where TRepository : IRepository<TClass>
{
    protected readonly TRepository Repository;

    // Supplied rather than named here: the CRUD below is generic over TClass and cannot say which
    // of the library, the venues or the users just moved.
    private readonly object _changeMessage;

    protected BaseRepositoryService(ILogger logger, TRepository repository, IMessageBroker? broker,
        object changeMessage)
        : base(logger, broker)
    {
        Repository = repository;
        _changeMessage = changeMessage;
    }

    public virtual async Task<TClass> CreateAsync(TClass entity)
    {
        var savedEntity = await Repository.CreateAsync(entity);

        Announce(_changeMessage);

        return savedEntity;
    }

    public virtual async Task<TClass?> ReadAsync(Guid id)
    {
        return await Repository.ReadAsync(id);
    }

    public virtual async Task UpdateAsync(TClass entity)
    {
        await Repository.UpdateAsync(entity);

        Announce(_changeMessage);
    }

    public virtual async Task<bool> DeleteAsync(Guid id)
    {
        var success = await Repository.DeleteAsync(id);

        if (success)
            Announce(_changeMessage);

        return success;
    }

    public virtual async Task<PaginatedResult<TClass>> ReadAllAsync(int pageNumber = 1, int pageSize = 0)
    {
        return await Repository.ReadAllAsync(pageNumber, pageSize);
    }

    public virtual Task<PaginatedResult<TClass>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort)
    {
        return Repository.ReadAllAsync(pageNumber, pageSize, sort);
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

    public virtual Task<PaginatedResult<TClass>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
    {
        return Repository.SearchAsync(query, pageNumber, pageSize, sort);
    }

    public virtual async Task<bool> HasAnyAsync()
    {
        return await Repository.HasAnyAsync();
    }
}
