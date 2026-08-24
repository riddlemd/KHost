using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;

namespace KHost.UnitTests.Domain.Services;

public class VenuesServiceTests : IDisposable
{
    private readonly ILogger<VenuesService> _logger = Substitute.For<ILogger<VenuesService>>();
    private readonly IVenuesRepository _repository;
    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly VenuesService _service;
    private readonly List<Venue> _venueStore = new();

    public VenuesServiceTests()
    {
        _repository = Substitute.For<IVenuesRepository>();
        SetupRepositoryDefaults();
        _service = new VenuesService(_logger, _repository, _cacheService, _broker);
    }

    private void SetupRepositoryDefaults()
    {
        _repository.CreateAsync(Arg.Any<Venue>())
            .Returns(call =>
            {
                var venue = call.Arg<Venue>();
                _venueStore.Add(venue);
                return Task.FromResult(venue);
            });

        _repository.ReadAsync(Arg.Any<Guid>())
            .Returns(call =>
            {
                var id = call.Arg<Guid>();
                return Task.FromResult(_venueStore.FirstOrDefault(v => v.Id == id));
            });

        _repository.UpdateAsync(Arg.Any<Venue>())
            .Returns(Task.CompletedTask);

        _repository.DeleteAsync(Arg.Any<Guid>())
            .Returns(call =>
            {
                var id = call.Arg<Guid>();
                var venue = _venueStore.FirstOrDefault(v => v.Id == id);
                if (venue is not null)
                    _venueStore.Remove(venue);
                return Task.FromResult(venue is not null);
            });

        _repository.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(call =>
            {
                var pageNumber = call.ArgAt<int>(0);
                var pageSize = call.ArgAt<int>(1);
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 50;
                if (pageSize > 1000) pageSize = 1000;
                var items = _venueStore.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
                var result = new PaginatedResult<Venue>
                {
                    Items = items,
                    TotalCount = _venueStore.Count,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                return Task.FromResult(result);
            });

        _repository.SearchAsync<object>(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<object>())
            .Returns(call =>
            {
                var query = call.ArgAt<string>(0);
                var pageNumber = call.ArgAt<int>(1);
                var pageSize = call.ArgAt<int>(2);
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 50;
                if (pageSize > 1000) pageSize = 1000;
                var filtered = string.IsNullOrWhiteSpace(query)
                    ? _venueStore
                    : _venueStore.Where(v => v.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                             v.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                var items = filtered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
                var result = new PaginatedResult<Venue>
                {
                    Items = items,
                    TotalCount = filtered.Count,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                return Task.FromResult(result);
            });

        _repository.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(call =>
            {
                var query = call.ArgAt<string>(0);
                var pageNumber = call.ArgAt<int>(1);
                var pageSize = call.ArgAt<int>(2);
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 50;
                if (pageSize > 1000) pageSize = 1000;
                var filtered = string.IsNullOrWhiteSpace(query)
                    ? _venueStore
                    : _venueStore.Where(v => v.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                             v.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                var items = filtered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
                var result = new PaginatedResult<Venue>
                {
                    Items = items,
                    TotalCount = filtered.Count,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                return Task.FromResult(result);
            });
    }

    public void Dispose()
    {
        _venueStore.Clear();
    }

    [Fact]
    public async Task NewService_StartsWithNoVenues()
    {
        var result = await _service.ReadAllAsync();
        Assert.Empty(result.Items);
        Assert.Null(_service.SelectedVenueId);
        Assert.Null(await _service.ReadSelectedVenueAsync());
    }

    [Fact]
    public async Task CreateAsync_AddsVenueToList()
    {
        var venue = new Venue { Name = "The Pub", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(venue);
        var result = await _service.ReadAllAsync();

        Assert.Single(result.Items);
        Assert.Equal("The Pub", created.Name);
        Assert.Contains(result.Items, v => v.Id == created.Id);
    }

    [Fact]
    public async Task CreateAsync_RaisesStateChanged()
    {
        var raised = false;
        using var subscription = _broker.Subscribe<VenuesChanged>(_ => raised = true);

        var venue = new Venue { Name = "The Pub", Notes = "", Enabled = true };
        await _service.CreateAsync(venue);

        Assert.True(raised);
    }

    [Fact]
    public async Task UpdateAsync_ChangesNameAndNotes()
    {
        var venue = new Venue { Name = "Original", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(venue);

        created.Name = "Updated";
        created.Notes = "Some notes";
        created.Enabled = false;
        await _service.UpdateAsync(created);

        var updated = await _service.ReadAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal("Some notes", updated.Notes);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesVenue()
    {
        var venue = new Venue { Name = "Real", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(venue);

        created.Name = "Updated";
        await _service.UpdateAsync(created);

        var updated = await _service.ReadAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.Name);
    }

    [Fact]
    public async Task DeleteAsync_RemovesVenue()
    {
        var venue = new Venue { Name = "The Pub", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(venue);

        await _service.DeleteAsync(created.Id);

        var result = await _service.ReadAllAsync();
        Assert.Empty(result.Items);
        Assert.DoesNotContain(result.Items, v => v.Id == created.Id);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAll_WhenQueryIsWhitespace()
    {
        var alpha = new Venue { Name = "Alpha", Notes = "", Enabled = true };
        var beta = new Venue { Name = "Beta", Notes = "", Enabled = true };
        await _service.CreateAsync(alpha);
        await _service.CreateAsync(beta);

        var result = await _service.SearchAsync("   ");

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_FiltersByName_CaseInsensitive()
    {
        var alpha = new Venue { Name = "Alpha Bar", Notes = "", Enabled = true };
        var beta = new Venue { Name = "Beta Pub", Notes = "", Enabled = true };
        await _service.CreateAsync(alpha);
        await _service.CreateAsync(beta);

        var result = await _service.SearchAsync("alpha");

        Assert.Single(result.Items);
        Assert.Equal("Alpha Bar", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchAsync_MatchesOnNotes()
    {
        var alpha = new Venue { Name = "Alpha", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(alpha);
        created.Notes = "has a keyword here";
        await _service.UpdateAsync(created);

        var beta = new Venue { Name = "Beta", Notes = "", Enabled = true };
        await _service.CreateAsync(beta);

        var result = await _service.SearchAsync("keyword");

        Assert.Single(result.Items);
        Assert.Equal("Alpha", result.Items[0].Name);
    }

    [Fact]
    public async Task ReadAsync_ReturnsVenue_WhenExists()
    {
        var venue = new Venue { Name = "The Pub", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(venue);

        var found = await _service.ReadAsync(created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found!.Id);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenMissing()
    {
        var result = await _service.ReadAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadByNameAsync_IsCaseInsensitive()
    {
        var venue = new Venue { Name = "The Pub", Notes = "", Enabled = true };
        await _service.CreateAsync(venue);

        var found = await _service.ReadByNameAsync("the pub");

        Assert.NotNull(found);
        Assert.Equal("The Pub", found!.Name);
    }

    [Fact]
    public async Task SelectVenueAsync_SetsSelectedVenueId()
    {
        var venue = new Venue { Name = "The Pub", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(venue);

        await _service.SelectVenueAsync(created.Id);

        Assert.Equal(created.Id, _service.SelectedVenueId);
        Assert.NotNull(await _service.ReadSelectedVenueAsync());
    }

    [Fact]
    public async Task SelectVenueAsync_Null_ClearsSelection()
    {
        var venue = new Venue { Name = "The Pub", Notes = "", Enabled = true };
        var created = await _service.CreateAsync(venue);
        await _service.SelectVenueAsync(created.Id);

        await _service.SelectVenueAsync(null);

        Assert.Null(_service.SelectedVenueId);
        Assert.Null(await _service.ReadSelectedVenueAsync());
    }

    [Fact]
    public async Task SelectVenueAsync_PersistsTheSelection()
    {
        var created = await _service.CreateAsync(new Venue { Name = "The Pub", Notes = "", Enabled = true });

        await _service.SelectVenueAsync(created.Id);

        await _cacheService.Received().SaveAsync<Guid?>("selected-venue", created.Id);
    }

    [Fact]
    public async Task InitializeAsync_RestoresThePersistedSelection()
    {
        var created = await _service.CreateAsync(new Venue { Name = "The Pub", Notes = "", Enabled = true });
        await _service.SelectVenueAsync(created.Id);
        CacheReturns(created.Id);

        var restored = MakeService();
        await restored.InitializeAsync();

        // Without this, everything keyed off the selected venue is inert after a restart.
        Assert.Equal(created.Id, restored.SelectedVenueId);
    }

    [Fact]
    public async Task InitializeAsync_FallsBackToFirstVenue_WhenNothingPersisted()
    {
        var created = await _service.CreateAsync(new Venue { Name = "The Pub", Notes = "", Enabled = true });

        var restored = MakeService();
        await restored.InitializeAsync();

        Assert.Equal(created.Id, restored.SelectedVenueId);
    }

    [Fact]
    public async Task InitializeAsync_FallsBack_WhenThePersistedVenueWasDeleted()
    {
        var kept = await _service.CreateAsync(new Venue { Name = "The Pub", Notes = "", Enabled = true });
        CacheReturns(Guid.NewGuid());

        var restored = MakeService();
        await restored.InitializeAsync();

        Assert.Equal(kept.Id, restored.SelectedVenueId);
    }

    [Fact]
    public async Task DeleteAsync_RefusesToRemoveTheSelectedVenue()
    {
        var first = await _service.CreateAsync(new Venue { Name = "The Pub", Notes = "", Enabled = true });
        await _service.CreateAsync(new Venue { Name = "The Club", Notes = "", Enabled = true });
        await _service.SelectVenueAsync(first.Id);

        var deleted = await _service.DeleteAsync(first.Id);

        Assert.False(deleted);
        Assert.Equal(first.Id, _service.SelectedVenueId);
        var result = await _service.ReadAllAsync();
        Assert.Contains(result.Items, v => v.Id == first.Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheVenueOnceTheHostSwitchesAway()
    {
        var first = await _service.CreateAsync(new Venue { Name = "The Pub", Notes = "", Enabled = true });
        var second = await _service.CreateAsync(new Venue { Name = "The Club", Notes = "", Enabled = true });
        await _service.SelectVenueAsync(first.Id);
        await _service.SelectVenueAsync(second.Id);

        var deleted = await _service.DeleteAsync(first.Id);

        Assert.True(deleted);
        Assert.Equal(second.Id, _service.SelectedVenueId);
    }

    [Fact]
    public async Task DeleteAsync_LeavesSelectionAlone_WhenADifferentVenueIsRemoved()
    {
        var first = await _service.CreateAsync(new Venue { Name = "The Pub", Notes = "", Enabled = true });
        var second = await _service.CreateAsync(new Venue { Name = "The Club", Notes = "", Enabled = true });
        await _service.SelectVenueAsync(first.Id);

        await _service.DeleteAsync(second.Id);

        Assert.Equal(first.Id, _service.SelectedVenueId);
    }

    private VenuesService MakeService() => new(_logger, _repository, _cacheService, _broker);

    private void CacheReturns(Guid id) =>
        _cacheService.LoadAsync<Guid?>("selected-venue").Returns(id);

    [Fact]
    public async Task SelectVenueAsync_RaisesStateChanged()
    {
        var raised = false;
        using var subscription = _broker.Subscribe<VenuesChanged>(_ => raised = true);

        await _service.SelectVenueAsync(Guid.NewGuid());

        Assert.True(raised);
    }
}
