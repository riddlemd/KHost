using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services.MediaPools;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.MediaPools;

public class MediaPoolServiceTests
{
    private readonly IMediaPoolRepository _repository = Substitute.For<IMediaPoolRepository>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly List<Venue> _venueRows = [];
    private readonly MediaPoolService _service;

    public MediaPoolServiceTests()
    {
        _repository.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(Task.FromResult<IReadOnlyList<MediaPool>>([]));

        _venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(_ => Task.FromResult(new PaginatedResult<Venue> { Items = [.. _venueRows] }));

        _service = new MediaPoolService(NullLogger<MediaPoolService>.Instance, _repository,
            _venues, _broker, random: new Random(1));
    }

    private static MediaPool Pool(Guid id, params MediaPoolEntry[] entries) => new()
    {
        Id = id,
        Name = "pool",
        SelectionMode = PoolSelectionMode.Sequential,
        NoRepeatCount = 0,
        Entries = [.. entries],
    };

    [Fact]
    public async Task SelectNextAsync_UnknownPool_ReturnsNull()
    {
        _repository.ReadWithEntriesAsync(Arg.Any<Guid>()).Returns(Task.FromResult<MediaPool?>(null));

        Assert.Null(await _service.SelectNextAsync(Guid.NewGuid(), venueId: null));
    }

    [Fact]
    public async Task SelectNextAsync_ReturnsATrackFromThePool()
    {
        var mediaId = Guid.NewGuid();
        var pool = Pool(Guid.NewGuid(), new MediaPoolEntry { MediaId = mediaId });

        _repository.ReadWithEntriesAsync(pool.Id).Returns(Task.FromResult<MediaPool?>(pool));

        Assert.Equal(mediaId, (await _service.SelectNextAsync(pool.Id, venueId: null))?.MediaId);
    }

    // The cursor lives on the service, so consecutive calls have to advance rather than each
    // starting the pool over.
    [Fact]
    public async Task SelectNextAsync_CalledTwice_AdvancesTheSequentialCursor()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var pool = Pool(Guid.NewGuid(),
            new MediaPoolEntry { MediaId = first, Position = 0 },
            new MediaPoolEntry { MediaId = second, Position = 1 });

        _repository.ReadWithEntriesAsync(pool.Id).Returns(Task.FromResult<MediaPool?>(pool));

        Assert.Equal(first, (await _service.SelectNextAsync(pool.Id, venueId: null))?.MediaId);
        Assert.Equal(second, (await _service.SelectNextAsync(pool.Id, venueId: null))?.MediaId);
    }

    [Fact]
    public async Task ResetSelection_SendsASequentialPoolBackToItsFirstEntry()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var pool = Pool(Guid.NewGuid(),
            new MediaPoolEntry { MediaId = first, Position = 0 },
            new MediaPoolEntry { MediaId = second, Position = 1 });

        _repository.ReadWithEntriesAsync(pool.Id).Returns(Task.FromResult<MediaPool?>(pool));

        await _service.SelectNextAsync(pool.Id, venueId: null);
        _service.ResetSelection(pool.Id);

        Assert.Equal(first, (await _service.SelectNextAsync(pool.Id, venueId: null))?.MediaId);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_UnknownPool_ReturnsFalse()
    {
        _repository.ReadWithEntriesAsync(Arg.Any<Guid>()).Returns(Task.FromResult<MediaPool?>(null));

        Assert.False(await _service.ReplaceEntriesAsync(Guid.NewGuid(), []));
        await _repository.DidNotReceive().ReplaceEntriesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<MediaPoolEntry>>());
    }

    [Fact]
    public async Task ReplaceEntriesAsync_EntriesThatCloseALoop_AreRefused()
    {
        var pool = Pool(Guid.NewGuid());
        _repository.ReadWithEntriesAsync(pool.Id).Returns(Task.FromResult<MediaPool?>(pool));

        var refused = await _service.ReplaceEntriesAsync(pool.Id,
            [new MediaPoolEntry { ChildPoolId = pool.Id }]);

        Assert.False(refused);
        await _repository.DidNotReceive().ReplaceEntriesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<MediaPoolEntry>>());
    }

    [Fact]
    public async Task ReplaceEntriesAsync_ValidEntries_AreSaved()
    {
        var pool = Pool(Guid.NewGuid());
        var mediaId = Guid.NewGuid();
        _repository.ReadWithEntriesAsync(pool.Id).Returns(Task.FromResult<MediaPool?>(pool));

        var saved = await _service.ReplaceEntriesAsync(pool.Id, [new MediaPoolEntry { MediaId = mediaId }]);

        Assert.True(saved);
        await _repository.Received(1).ReplaceEntriesAsync(pool.Id,
            Arg.Is<IReadOnlyList<MediaPoolEntry>>(e => e.Single().MediaId == mediaId));
    }

    [Fact]
    public async Task ReplaceEntriesAsync_AnnouncesMediaPoolsChanged()
    {
        var pool = Pool(Guid.NewGuid());
        _repository.ReadWithEntriesAsync(pool.Id).Returns(Task.FromResult<MediaPool?>(pool));

        var raised = 0;
        using var subscription = _broker.Subscribe<MediaPoolsChanged>(_ => raised++);

        await _service.ReplaceEntriesAsync(pool.Id, [new MediaPoolEntry { MediaId = Guid.NewGuid() }]);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_RefusedEntries_DoNotAnnounceMediaPoolsChanged()
    {
        var pool = Pool(Guid.NewGuid());
        _repository.ReadWithEntriesAsync(pool.Id).Returns(Task.FromResult<MediaPool?>(pool));

        var raised = 0;
        using var subscription = _broker.Subscribe<MediaPoolsChanged>(_ => raised++);

        await _service.ReplaceEntriesAsync(pool.Id, [new MediaPoolEntry { ChildPoolId = pool.Id }]);

        Assert.Equal(0, raised);
    }

    private Venue VenueNaming(Guid? adPoolId = null, Guid? breakMusicPoolId = null)
    {
        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            Name = "The Bar",
            Settings = new Venue.VenueSettings { AdPoolId = adPoolId, BreakMusicPoolId = breakMusicPoolId },
        };

        _venueRows.Add(venue);
        return venue;
    }

    // Left behind, the venue dialog shows "none" over an id it cannot clear.
    [Fact]
    public async Task DeleteAsync_APlaylistAVenueRunsAdsFrom_ForgetsItOnThatVenue()
    {
        var poolId = Guid.NewGuid();
        _repository.DeleteAsync(poolId).Returns(Task.FromResult(true));
        var venue = VenueNaming(adPoolId: poolId);

        await _service.DeleteAsync(poolId);

        Assert.Null(venue.Settings.AdPoolId);
        await _venues.Received(1).UpdateAsync(venue);
    }

    [Fact]
    public async Task DeleteAsync_APlaylistAVenuePlaysBetweenSingers_ForgetsItOnThatVenue()
    {
        var poolId = Guid.NewGuid();
        _repository.DeleteAsync(poolId).Returns(Task.FromResult(true));
        var venue = VenueNaming(breakMusicPoolId: poolId);

        await _service.DeleteAsync(poolId);

        Assert.Null(venue.Settings.BreakMusicPoolId);
        await _venues.Received(1).UpdateAsync(venue);
    }

    [Fact]
    public async Task DeleteAsync_AVenueNamingAnotherPlaylist_IsLeftAlone()
    {
        var poolId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        _repository.DeleteAsync(poolId).Returns(Task.FromResult(true));
        var venue = VenueNaming(adPoolId: otherId, breakMusicPoolId: otherId);

        await _service.DeleteAsync(poolId);

        Assert.Equal(otherId, venue.Settings.AdPoolId);
        Assert.Equal(otherId, venue.Settings.BreakMusicPoolId);
        await _venues.DidNotReceive().UpdateAsync(Arg.Any<Venue>());
    }

    [Fact]
    public async Task DeleteAsync_ARefusedDelete_LeavesVenuesAlone()
    {
        var poolId = Guid.NewGuid();
        _repository.DeleteAsync(poolId).Returns(Task.FromResult(false));
        var venue = VenueNaming(adPoolId: poolId);

        Assert.False(await _service.DeleteAsync(poolId));

        Assert.Equal(poolId, venue.Settings.AdPoolId);
        await _venues.DidNotReceive().UpdateAsync(Arg.Any<Venue>());
    }
}
