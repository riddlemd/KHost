using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;

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
        _service = new TipsService(_logger, _repository, _venuesService, new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task CreateAsync_StampsTheSelectedVenue()
    {
        var venueId = Guid.NewGuid();
        _venuesService.SelectedVenueId.Returns(venueId);

        var tip = await _service.CreateAsync(new Tip { UserId = Guid.NewGuid(), AmountInCents = 500 });

        Assert.Equal(venueId, tip.VenueId);
    }

    [Fact]
    public async Task CreateAsync_KeepsAnExplicitVenueOverTheSelectedOne()
    {
        var explicitVenueId = Guid.NewGuid();
        _venuesService.SelectedVenueId.Returns(Guid.NewGuid());

        var tip = await _service.CreateAsync(
            new Tip { UserId = Guid.NewGuid(), AmountInCents = 500, VenueId = explicitVenueId });

        Assert.Equal(explicitVenueId, tip.VenueId);
    }

    [Fact]
    public async Task CreateAsync_LeavesVenueNullWhenNoneIsSelected()
    {
        _venuesService.SelectedVenueId.Returns((Guid?)null);

        var tip = await _service.CreateAsync(new Tip { UserId = Guid.NewGuid(), AmountInCents = 500 });

        Assert.Null(tip.VenueId);
    }

    [Fact]
    public async Task CreateAsync_WithNoSinger_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateAsync(new Tip { UserId = Guid.Empty, AmountInCents = 500 }));

        await _repository.DidNotReceive().CreateAsync(Arg.Any<Tip>());
    }

    [Fact]
    public async Task UpdateAsync_WithNoSinger_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateAsync(new Tip { UserId = Guid.Empty, AmountInCents = 500 }));

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Tip>());
    }

    [Fact]
    public async Task UpdateAsync_WithASinger_ReachesTheRepository()
    {
        var tip = new Tip { UserId = Guid.NewGuid(), AmountInCents = 500 };

        await _service.UpdateAsync(tip);

        await _repository.Received(1).UpdateAsync(tip);
    }
}