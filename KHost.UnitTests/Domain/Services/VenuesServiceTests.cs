using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Models;
using KHost.Domain.Services;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class VenuesServiceTests : IDisposable
{
    private const string _cacheDir = "./cache";
    private readonly IOptionsMonitor<VenuesService.ServiceOptions> _options =
        Substitute.For<IOptionsMonitor<VenuesService.ServiceOptions>>();
    private readonly ICacheService _cacheService;
    private readonly VenuesService _service;

    public VenuesServiceTests()
    {
        var cacheOptions = Substitute.For<IOptionsMonitor<JsonFileCacheService.ServiceOptions>>();
        cacheOptions.CurrentValue.Returns(new JsonFileCacheService.ServiceOptions { CachePath = _cacheDir });
        _cacheService = new JsonFileCacheService(cacheOptions);
        _service = new VenuesService(_options, _cacheService);
    }

    public void Dispose()
    {
        var cacheFile = Path.Combine(_cacheDir, "venues.json");
        if (File.Exists(cacheFile))
            File.Delete(cacheFile);
    }

    [Fact]
    public void NewService_StartsWithDefaultVenue()
    {
        Assert.Single(_service.Venues);
        Assert.Equal("Default Venue", _service.Venues[0].Name);
        Assert.Null(_service.SelectedVenueId);
        Assert.Null(_service.SelectedVenue);
    }

    [Fact]
    public async Task CreateAsync_AddsVenueToList()
    {
        var venue = await _service.CreateAsync("The Pub");

        Assert.Equal(2, _service.Venues.Count);
        Assert.Equal("The Pub", venue.Name);
        Assert.Contains(_service.Venues, v => v.Id == venue.Id);
    }

    [Fact]
    public async Task CreateAsync_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += () => raised = true;

        await _service.CreateAsync("The Pub");

        Assert.True(raised);
    }

    [Fact]
    public async Task CreateAsync_PersistsStateToFile()
    {
        await _service.CreateAsync("The Pub");

        var cacheFile = Path.Combine(_cacheDir, "venues.json");
        Assert.True(File.Exists(cacheFile));
        var content = await File.ReadAllTextAsync(cacheFile);
        Assert.Contains("The Pub", content);
    }

    [Fact]
    public async Task UpdateAsync_ChangesNameAndNotes()
    {
        var venue = await _service.CreateAsync("Original");

        await _service.UpdateAsync(venue.Id, "Updated", "Some notes", false);

        var updated = await _service.ReadByIdAsync(venue.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal("Some notes", updated.Notes);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task UpdateAsync_IgnoresUnknownId()
    {
        var venue = await _service.CreateAsync("Real");

        await _service.UpdateAsync(Guid.NewGuid(), "Ghost");

        var updated = await _service.ReadByIdAsync(venue.Id);
        Assert.NotNull(updated);
        Assert.Equal("Real", updated!.Name);
    }

    [Fact]
    public async Task RemoveAsync_RemovesVenue()
    {
        var venue = await _service.CreateAsync("The Pub");

        await _service.RemoveAsync(venue.Id);

        Assert.Single(_service.Venues);
        Assert.DoesNotContain(_service.Venues, v => v.Id == venue.Id);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAll_WhenQueryIsWhitespace()
    {
        await _service.CreateAsync("Alpha");
        await _service.CreateAsync("Beta");

        var result = await _service.SearchAsync("   ");

        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_FiltersByName_CaseInsensitive()
    {
        await _service.CreateAsync("Alpha Bar");
        await _service.CreateAsync("Beta Pub");

        var result = await _service.SearchAsync("alpha");

        Assert.Single(result.Items);
        Assert.Equal("Alpha Bar", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchAsync_MatchesOnNotes()
    {
        var alpha = await _service.CreateAsync("Alpha");
        await _service.UpdateAsync(alpha.Id, "Alpha", "has a keyword here");
        await _service.CreateAsync("Beta");

        var result = await _service.SearchAsync("keyword");

        Assert.Single(result.Items);
        Assert.Equal("Alpha", result.Items[0].Name);
    }

    [Fact]
    public async Task ReadByIdAsync_ReturnsVenue_WhenExists()
    {
        var venue = await _service.CreateAsync("The Pub");

        var found = await _service.ReadByIdAsync(venue.Id);

        Assert.NotNull(found);
        Assert.Equal(venue.Id, found!.Id);
    }

    [Fact]
    public async Task ReadByIdAsync_ReturnsNull_WhenMissing()
    {
        var result = await _service.ReadByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadByNameAsync_IsCaseInsensitive()
    {
        await _service.CreateAsync("The Pub");

        var found = await _service.ReadByNameAsync("the pub");

        Assert.NotNull(found);
        Assert.Equal("The Pub", found!.Name);
    }

    [Fact]
    public async Task SelectVenueAsync_SetsSelectedVenueId()
    {
        var venue = await _service.CreateAsync("The Pub");

        await _service.SelectVenueAsync(venue.Id);

        Assert.Equal(venue.Id, _service.SelectedVenueId);
        Assert.NotNull(_service.SelectedVenue);
        Assert.Equal(venue.Id, _service.SelectedVenue!.Id);
    }

    [Fact]
    public async Task SelectVenueAsync_Null_ClearsSelection()
    {
        var venue = await _service.CreateAsync("The Pub");
        await _service.SelectVenueAsync(venue.Id);

        await _service.SelectVenueAsync(null);

        Assert.Null(_service.SelectedVenueId);
        Assert.Null(_service.SelectedVenue);
    }

    [Fact]
    public async Task SelectVenueAsync_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += () => raised = true;

        await _service.SelectVenueAsync(Guid.NewGuid());

        Assert.True(raised);
    }
}
