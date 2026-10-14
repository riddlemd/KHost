using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public abstract class BaseSearchableRepositoryService<TClass, TRepository> : BaseRepositoryService<TClass, TRepository>
    where TClass : RepositoryModel
    where TRepository : IRepository<TClass>, ISearchable<TClass>
{
    protected BaseSearchableRepositoryService(ILogger logger, TRepository repository, IMessageBroker broker,
        object changeMessage)
        : base(logger, repository, broker, changeMessage)
    {
    }

    public override Task<PaginatedResult<TClass>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50)
        => Repository.SearchAsync(query, pageNumber, pageSize);

    public override Task<PaginatedResult<TClass>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
        => Repository.SearchAsync(query, pageNumber, pageSize, sort);
}
