using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class TipsServiceTests
{
    private readonly ILogger<TipsService> _logger = Substitute.For<ILogger<TipsService>>();
    private readonly ITipsRepository _repository = Substitute.For<ITipsRepository>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly TipsService _service;

    public TipsServiceTests()
    {
        _repository.CreateAsync(Arg.Any<Tip>()).Returns(call => Task.FromResult(call.Arg<Tip>()));
        _service = new TipsService(_logger, _repository, _venuesService);
    }

    [Fact]
    public async Task CreateAsync_StampsTheSelectedVenue()
    {
        var venueId = Guid.NewGuid();
        _venuesService.SelectedVenueId.Returns(venueId);

        var tip = await _service.CreateAsync(new Tip { UserId = Guid.NewGuid(), Amount = 5m });

        Assert.Equal(venueId, tip.VenueId);
    }

    [Fact]
    public async Task CreateAsync_KeepsAnExplicitVenueOverTheSelectedOne()
    {
        var explicitVenueId = Guid.NewGuid();
        _venuesService.SelectedVenueId.Returns(Guid.NewGuid());

        var tip = await _service.CreateAsync(
            new Tip { UserId = Guid.NewGuid(), Amount = 5m, VenueId = explicitVenueId });

        Assert.Equal(explicitVenueId, tip.VenueId);
    }

    [Fact]
    public async Task CreateAsync_LeavesVenueNullWhenNoneIsSelected()
    {
        _venuesService.SelectedVenueId.Returns((Guid?)null);

        var tip = await _service.CreateAsync(new Tip { UserId = Guid.NewGuid(), Amount = 5m });

        Assert.Null(tip.VenueId);
    }
}
