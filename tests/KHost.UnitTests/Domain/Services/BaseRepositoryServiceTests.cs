using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class BaseRepositoryServiceTests
{
    public sealed class TestEntity : RepositoryModel
    {
    }

    public sealed class TestRepositoryService(ILogger logger, IRepository<TestEntity> repository, IMessageBroker broker)
        : BaseRepositoryService<TestEntity, IRepository<TestEntity>>(logger, repository, broker)
    {
        protected override object? StateChangedMessage => new TestEntityChanged();
    }

    public sealed record TestEntityChanged;

    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly IRepository<TestEntity> _repository = Substitute.For<IRepository<TestEntity>>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly TestRepositoryService _service;

    public BaseRepositoryServiceTests()
    {
        _service = new TestRepositoryService(_logger, _repository, _broker);
    }

    [Fact]
    public async Task DeleteAsync_InvokesStateChanged_WhenRepositoryReturnsTrue()
    {
        _repository.DeleteAsync(Arg.Any<Guid>()).Returns(Task.FromResult(true));
        int count = 0;
        using var subscription = _broker.Subscribe<TestEntityChanged>(_ => count++);

        await _service.DeleteAsync(Guid.NewGuid());

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotInvokeStateChanged_WhenRepositoryReturnsFalse()
    {
        _repository.DeleteAsync(Arg.Any<Guid>()).Returns(Task.FromResult(false));
        int count = 0;
        using var subscription = _broker.Subscribe<TestEntityChanged>(_ => count++);

        await _service.DeleteAsync(Guid.NewGuid());

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrueFromRepository()
    {
        _repository.DeleteAsync(Arg.Any<Guid>()).Returns(Task.FromResult(true));

        var result = await _service.DeleteAsync(Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseFromRepository()
    {
        _repository.DeleteAsync(Arg.Any<Guid>()).Returns(Task.FromResult(false));

        var result = await _service.DeleteAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task CreateAsync_DelegatesToRepository()
    {
        var entity = new TestEntity();
        _repository.CreateAsync(entity).Returns(Task.FromResult(entity));

        var result = await _service.CreateAsync(entity);

        Assert.Same(entity, result);
        await _repository.Received(1).CreateAsync(entity);
    }

    [Fact]
    public async Task ReadAsync_DelegatesToRepository()
    {
        var entity = new TestEntity();
        _repository.ReadAsync(entity.Id).Returns(Task.FromResult<TestEntity?>(entity));

        var result = await _service.ReadAsync(entity.Id);

        Assert.Same(entity, result);
    }

    [Fact]
    public async Task UpdateAsync_DelegatesToRepository()
    {
        var entity = new TestEntity();
        _repository.UpdateAsync(entity).Returns(Task.CompletedTask);

        await _service.UpdateAsync(entity);

        await _repository.Received(1).UpdateAsync(entity);
    }

    [Fact]
    public async Task ReadAllAsync_DelegatesToRepository()
    {
        var paginated = new PaginatedResult<TestEntity> { Items = [], TotalCount = 0, PageNumber = 1, PageSize = 50 };
        _repository.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(Task.FromResult(paginated));

        var result = await _service.ReadAllAsync();

        Assert.Same(paginated, result);
    }
}
