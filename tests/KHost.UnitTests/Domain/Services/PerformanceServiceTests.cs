using KHost.Domain.Services.MediaLifetime;
using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging.Messages;

namespace KHost.UnitTests.Domain.Services;

public class PerformanceServiceTests
{
    private readonly List<Performance> _performanceDb = [];
    private readonly IPerformancesRepository _repository = Substitute.For<IPerformancesRepository>();
    private readonly ILogger<PerformanceService> _logger = Substitute.For<ILogger<PerformanceService>>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IUsersService _usersService = Substitute.For<IUsersService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IInteractionDispatcher _interactions = Substitute.For<IInteractionDispatcher>();
    private readonly IDownloadsService _downloadsService = Substitute.For<IDownloadsService>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly Venue _venue = new() { Name = "Test Venue" };
    private readonly PerformanceService _service;

    public PerformanceServiceTests()
    {
        _repository.CreateAsync(Arg.Any<Performance>())
            .Returns(args =>
            {
                var perf = (Performance)args[0];
                _performanceDb.Add(perf);
                return Task.FromResult(perf);
            });

        _repository.ReadAsync(Arg.Any<Guid>())
            .Returns(args => Task.FromResult(_performanceDb.FirstOrDefault(p => p.Id == (Guid)args[0])));

        _repository.UpdateAsync(Arg.Any<Performance>())
            .Returns(args =>
            {
                var perf = (Performance)args[0];
                var existing = _performanceDb.FirstOrDefault(p => p.Id == perf.Id);
                if (existing != null)
                {
                    existing.QueuePosition = perf.QueuePosition;
                }
                return Task.FromResult(perf);
            });

        _repository.ReadNextQueuePositionForSingerAsync(Arg.Any<Guid>())
            .Returns(args =>
            {
                var singerId = (Guid)args[0];
                var maxPosition = _performanceDb
                    .Where(p => p.SingerId == singerId && p.QueuePosition.HasValue)
                    .Max(p => p.QueuePosition) ?? 0;
                return Task.FromResult(maxPosition + 1);
            });

        _repository.ReadQueuedAsync()
            .Returns(_ =>
            {
                var queued = _performanceDb
                    .Where(p => p.QueuePosition.HasValue)
                    .OrderBy(p => p.QueuePosition)
                    .ToList();
                return Task.FromResult(queued);
            });

        _repository.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(_ =>
            {
                var allPerfs = new PaginatedResult<Performance>
                {
                    Items = _performanceDb,
                    PageNumber = 1,
                    PageSize = 1000,
                    TotalCount = _performanceDb.Count
                };
                return Task.FromResult(allPerfs);
            });

        _repository.ReadBySingerIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>())
            .Returns(args =>
            {
                var singerId = (Guid)args[0];
                var filter = (PerformanceFilter)args[3];

                var query = _performanceDb.Where(p => p.SingerId == singerId);

                if (filter.HasFlag(PerformanceFilter.Queued) && !filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => p.QueuePosition.HasValue);
                else if (!filter.HasFlag(PerformanceFilter.Queued) && filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => !p.QueuePosition.HasValue);

                var items = query.OrderBy(p => p.QueuePosition).ToList();
                return Task.FromResult(new PaginatedResult<Performance>
                {
                    Items = items,
                    TotalCount = items.Count,
                    PageNumber = 1,
                    PageSize = 0
                });
            });

        _repository.ReadByMediaIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>())
            .Returns(args =>
            {
                var mediaId = (Guid)args[0];
                var filter = (PerformanceFilter)args[3];

                var query = _performanceDb.Where(p => p.MediaId == mediaId);

                if (filter.HasFlag(PerformanceFilter.Queued) && !filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => p.QueuePosition.HasValue);
                else if (!filter.HasFlag(PerformanceFilter.Queued) && filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => !p.QueuePosition.HasValue);

                var items = query.ToList();
                return Task.FromResult(new PaginatedResult<Performance>
                {
                    Items = items,
                    TotalCount = items.Count,
                    PageNumber = 1,
                    PageSize = 0
                });
            });

        _repository.DeleteAsync(Arg.Any<Guid>())
            .Returns(args =>
            {
                var id = (Guid)args[0];
                var existing = _performanceDb.FirstOrDefault(p => p.Id == id);
                if (existing is null) return Task.FromResult(false);

                _performanceDb.Remove(existing);
                return Task.FromResult(true);
            });

        _venuesService.ReadSelectedVenueAsync().Returns(_venue);

        _service = new PerformanceService(_logger, _repository, _mediaService, _usersService, _venuesService, _interactions, _downloadsService, _services, _broker);
    }

    [Fact]
    public async Task NewService_HasEmptyQueues()
    {
        var singerId = Guid.NewGuid();
        var performances = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);

        Assert.Empty(performances.Items);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_AddsMediaToSingerQueue()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        var performance = Assert.IsType<Performance>(
            await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId }));

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Single(queued.Items);
        Assert.Equal(performance.Id, queued.Items[0].Id);
        Assert.Equal(singerId, queued.Items[0].SingerId);
        Assert.Equal(mediaId, queued.Items[0].MediaId);
        Assert.Equal(1, queued.Items[0].QueuePosition);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_StampsACreatedDate_WhenTheCallerDidNot()
    {
        var before = DateTime.UtcNow;

        var performance = await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });

        // History is ordered on this. Unstamped, a performance sorts below every real one and the
        // singer's newest song is the one missing from the first page of their history.
        Assert.NotNull(performance);
        Assert.InRange(performance.CreatedDate, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_KeepsACreatedDateTheCallerSupplied()
    {
        var sungAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        var performance = await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid(), CreatedDate = sungAt });

        Assert.Equal(sungAt, performance!.CreatedDate);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_AnnouncesPerformancesChanged()
    {
        var raised = false;
        using var subscription = _broker.Subscribe<PerformancesChanged>(_ => raised = true);

        await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });

        Assert.True(raised);
    }

    /// <summary>Refused at the queue rather than at the microphone. A song whose provider will not
    /// let it play is one nobody can sing, and a host who learns that now can pick another.</summary>
    [Fact]
    public async Task CreateAndEnqueueAsync_MediaItsProviderRefuses_IsNotQueued()
    {
        var mediaId = ArrangeGatedMedia(new PlaybackGateResult(false, "Sign in to the provider."));
        var singerId = Guid.NewGuid();

        var performance = await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });

        Assert.Null(performance);
        Assert.Empty((await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued)).Items);
    }

    /// <summary>A refusal with nothing on screen to explain it reads as the console ignoring the
    /// click, so the provider's own words are what the host is shown.</summary>
    [Fact]
    public async Task CreateAndEnqueueAsync_MediaItsProviderRefuses_FlashesTheProvidersReason()
    {
        var flash = Substitute.For<IFlashService>();
        _services.GetService(typeof(IFlashService)).Returns(flash);
        var mediaId = ArrangeGatedMedia(new PlaybackGateResult(false, "Sign in to the provider."));

        await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });

        flash.Received(1).Show("Sign in to the provider.", FlashType.Warning);
    }

    /// <summary>A gate may refuse without saying why, and a flash of nothing reads as the console
    /// ignoring the click. The host gets a sentence either way.</summary>
    [Fact]
    public async Task CreateAndEnqueueAsync_AGateThatRefusesWithoutAReason_FlashesSomethingReadable()
    {
        var flash = Substitute.For<IFlashService>();
        _services.GetService(typeof(IFlashService)).Returns(flash);
        var mediaId = ArrangeGatedMedia(new PlaybackGateResult(false, null));

        await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });

        flash.Received(1).Show(
            Arg.Is<string>(message => !string.IsNullOrWhiteSpace(message)), FlashType.Warning);
    }

    /// <summary>The gate is asked about the queue, not the play: a provider answers a queue by asking
    /// the host to sign in, which is the whole point of refusing this early.</summary>
    [Fact]
    public async Task CreateAndEnqueueAsync_AsksTheGateAboutQueueing()
    {
        var gate = Substitute.For<IMediaGateService>();
        gate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>())
            .Returns(PlaybackGateResult.Ok);
        _services.GetService(typeof(IMediaGateService)).Returns(gate);
        var mediaId = Guid.NewGuid();
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/library/song.mp4", Title = "Song" });

        await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });

        await gate.Received().EvaluateAsync(MediaAction.Queue, Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The control for the refusals above: an allowed song still reaches the queue, so
    /// those tests are reading the gate rather than an enqueue that fails for its own reasons.
    /// </summary>
    [Fact]
    public async Task CreateAndEnqueueAsync_MediaItsProviderAllows_IsQueued()
    {
        var mediaId = ArrangeGatedMedia(PlaybackGateResult.Ok);

        Assert.NotNull(await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId }));
    }

    /// <summary>A gate that throws must not stop a host queueing the rest of the night.</summary>
    [Fact]
    public async Task CreateAndEnqueueAsync_AGateThatThrows_StillQueues()
    {
        var gate = Substitute.For<IMediaGateService>();
        gate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>())
            .Returns<PlaybackGateResult>(_ => throw new InvalidOperationException("the plugin fell over"));
        _services.GetService(typeof(IMediaGateService)).Returns(gate);
        var mediaId = Guid.NewGuid();
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/library/song.mp4", Title = "Song" });

        Assert.NotNull(await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId }));
    }

    /// <summary>A media row and a gate that answers <paramref name="verdict"/> for it.</summary>
    private Guid ArrangeGatedMedia(PlaybackGateResult verdict)
    {
        var mediaId = Guid.NewGuid();
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/library/song.mp4", Title = "Song" });

        var gate = Substitute.For<IMediaGateService>();
        gate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(verdict);
        _services.GetService(typeof(IMediaGateService)).Returns(gate);

        return mediaId;
    }

    [Fact]
    public async Task DequeueAsync_RemovesMedia()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await EnqueueForAsync(singerId);
        var perf2 = await EnqueueForAsync(singerId);

        await _service.DequeueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Single(queued.Items);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesADownloadingPerformancesMedia_CancelsThatMediasImport()
    {
        var singerId = Guid.NewGuid();
        var performance = await EnqueueForAsync(singerId);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Downloading });

        await _service.DeleteAsync(performance.Id);

        await _downloadsService.Received(1).CancelAsync(performance.MediaId);
    }

    /// <summary>Two guests' picks of a song still arriving share its row; taking one off must not
    /// kill the download the other is waiting on.</summary>
    [Fact]
    public async Task DeleteAsync_ADownloadAnotherQueuedTurnShares_IsNotCancelled()
    {
        var mediaId = Guid.NewGuid();
        var removed = Assert.IsType<Performance>(await EnqueueMediaAsync(Guid.NewGuid(), mediaId));
        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Downloading });

        await _service.DeleteAsync(removed.Id);

        await _downloadsService.DidNotReceive().CancelAsync(Arg.Any<Guid>());
    }

    /// <summary>A turn already sung does not hold a download open: only the queue is waiting.</summary>
    [Fact]
    public async Task DeleteAsync_ADownloadOnlyASungTurnShares_IsCancelled()
    {
        var mediaId = Guid.NewGuid();
        var sung = Assert.IsType<Performance>(await EnqueueMediaAsync(Guid.NewGuid(), mediaId));
        sung.QueuePosition = null;
        var removed = Assert.IsType<Performance>(await EnqueueMediaAsync(Guid.NewGuid(), mediaId));
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Downloading });

        await _service.DeleteAsync(removed.Id);

        await _downloadsService.Received(1).CancelAsync(mediaId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAProcessingPerformancesMedia_CancelsThatMediasImport()
    {
        var singerId = Guid.NewGuid();
        var performance = await EnqueueForAsync(singerId);
        // Still in flight, just past the download half; dequeuing has to stop the render too.
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.khv", Title = "Song", Status = MediaStatus.Processing });

        await _service.DeleteAsync(performance.Id);

        await _downloadsService.Received(1).CancelAsync(performance.MediaId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAReadyPerformancesMedia_DoesNotCancelAnyImport()
    {
        var singerId = Guid.NewGuid();
        var performance = await EnqueueForAsync(singerId);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Ready });

        await _service.DeleteAsync(performance.Id);

        await _downloadsService.DidNotReceive().CancelAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DeleteAsync_RemovesThePerformance_RegardlessOfMediaStatus()
    {
        var singerId = Guid.NewGuid();
        var performance = await EnqueueForAsync(singerId);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Ready });

        var deleted = await _service.DeleteAsync(performance.Id);

        Assert.True(deleted);
        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Empty(queued.Items);
    }

    [Fact]
    public async Task DeleteAsync_NoSuchPerformance_DoesNotCancelAnyImport()
    {
        await _service.DeleteAsync(Guid.NewGuid());

        await _downloadsService.DidNotReceive().CancelAsync(Arg.Any<Guid>());
    }

    // A dedicated substitute, not the shared one: reconfiguring DeleteAsync for one id re-triggers
    // the shared repository's existing Arg.Any callback as a side effect, deleting the row first.
    [Fact]
    public async Task DeleteAsync_RepositoryDeleteFails_DoesNotCancelAnyImport()
    {
        var repository = Substitute.For<IPerformancesRepository>();
        var performance = new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() };
        repository.ReadAsync(performance.Id).Returns(performance);
        repository.DeleteAsync(performance.Id).Returns(false);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Downloading });

        var service = new PerformanceService(_logger, repository, _mediaService, _usersService, _venuesService, _interactions, _downloadsService, _services, _broker);

        var deleted = await service.DeleteAsync(performance.Id);

        Assert.False(deleted);
        await _downloadsService.DidNotReceive().CancelAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task MoveUpInQueueAsync_SwapsWithPrevious()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await EnqueueForAsync(singerId);
        var perf2 = await EnqueueForAsync(singerId);

        await _service.MoveUpInQueueAsync(singerId, perf2.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf1.Id, queued.Items[1].Id);
    }

    [Fact]
    public async Task MoveDownInQueueAsync_SwapsWithNext()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await EnqueueForAsync(singerId);
        var perf2 = await EnqueueForAsync(singerId);

        await _service.MoveDownInQueueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf1.Id, queued.Items[1].Id);
    }

    [Fact]
    public async Task MoveToIndexAsync_DropsTheSongAtTheIndexAndClosesTheGap()
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);
        var third = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, first.Id, 2);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal([second.Id, third.Id, first.Id], queued.Items.Select(p => p.Id));
        // Positions, not just order: they are what everything else reads the queue back by, and
        // enqueueing takes the next one from their maximum.
        Assert.Equal([1, 2, 3], queued.Items.Select(p => p.QueuePosition));
    }

    [Fact]
    public async Task MoveToIndexAsync_MovesASongUpTheQueue()
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);
        var third = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, third.Id, 0);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal([third.Id, first.Id, second.Id], queued.Items.Select(p => p.Id));
    }

    // The row a drag lands on belongs to the whole table, and the queue can shrink mid-drag.
    [Theory]
    [InlineData(-3)]
    [InlineData(99)]
    public async Task MoveToIndexAsync_ClampsAnIndexPastEitherEnd(int newIndex)
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, first.Id, newIndex);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        var expected = newIndex < 0 ? new[] { first.Id, second.Id } : [second.Id, first.Id];
        Assert.Equal(expected, queued.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task MoveToIndexAsync_LeavesAnotherSingersQueueAlone()
    {
        var singerId = Guid.NewGuid();
        var otherSinger = Guid.NewGuid();
        var mine = await EnqueueForAsync(singerId);
        await EnqueueForAsync(singerId);
        await EnqueueForAsync(otherSinger);
        await EnqueueForAsync(otherSinger);
        // Read now, not after: the repository hands back the same instances, so a post-move compare
        // would pass however badly they were renumbered; two songs each so renumbering can't coincide.
        var before = (await _service.ReadBySingerIdAsync(otherSinger, filter: PerformanceFilter.Queued))
            .Items.Select(p => p.QueuePosition).ToList();

        await _service.MoveToIndexAsync(singerId, mine.Id, 1);

        var others = await _service.ReadBySingerIdAsync(otherSinger, filter: PerformanceFilter.Queued);
        Assert.Equal(before, others.Items.Select(p => p.QueuePosition));
    }

    [Fact]
    public async Task MoveToIndexAsync_IgnoresAPerformanceThatIsNotQueuedForThatSinger()
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, Guid.NewGuid(), 0);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal([first.Id, second.Id], queued.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task DeleteAllQueuedAsync_DelegatesToRepository()
    {
        _repository.DeleteAllQueuedAsync().Returns(Task.CompletedTask);

        await _service.DeleteAllQueuedAsync();

        await _repository.Received(1).DeleteAllQueuedAsync();
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_DoesNotWarn_WhenVenueHasWarningDisabled()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, mediaId);

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_Warns_WhenSongIsAlreadyQueued()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.Received(1).RequestAsync(
            Arg.Is<ConfirmDuplicateSongRequest>(r => r.TimesAlreadyQueued == 1 && r.SungWithinHours == null));
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_DoesNotWarn_WhenSongIsNotADuplicate()
    {
        _venue.Settings.WarnOnDuplicateSong = true;

        await EnqueueMediaAsync(Guid.NewGuid(), Guid.NewGuid());

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_ReturnsNullAndQueuesNothing_WhenWarningDeclined()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        _interactions.RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>()).Returns(false);
        var otherSinger = Guid.NewGuid();
        var declined = await EnqueueMediaAsync(otherSinger, mediaId);

        Assert.Null(declined);
        await _interactions.Received(1).RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
        Assert.Empty((await _service.ReadBySingerIdAsync(otherSinger, filter: PerformanceFilter.Queued)).Items);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_SongTheSingerAlreadyHasQueued_IsRefused()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, mediaId);

        var second = await EnqueueMediaAsync(singerId, mediaId);

        Assert.Null(second);
        Assert.Single((await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued)).Items);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_Queued_CarriesTheSavedPerformance()
    {
        var performance = new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() };

        var result = await _service.TryCreateAndEnqueueAsync(performance);

        Assert.Equal(EnqueueResultType.Queued, result.Type);
        Assert.Same(performance, result.Performance);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_SongTheSingerAlreadyHasQueued_SaysSo()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, mediaId);

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });

        Assert.Equal(EnqueueResultType.AlreadyQueued, result.Type);
        Assert.Null(result.Performance);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_TwoTapsWhileTheGateIsAsked_TheSecondSaysAlreadyQueued()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var gateAsked = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource<PlaybackGateResult>();
        var asked = 0;
        var gates = Substitute.For<IMediaGateService>();
        gates.EvaluateAsync(MediaAction.Queue, Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref asked) == 2)
                gateAsked.SetResult();
            return releaseGate.Task;
        });
        _services.GetService(typeof(IMediaGateService)).Returns(gates);
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/library/song.mp4", Title = "Song" });

        var first = _service.TryCreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });
        var second = _service.TryCreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });
        await gateAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseGate.SetResult(PlaybackGateResult.Ok);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(
            [EnqueueResultType.Queued, EnqueueResultType.AlreadyQueued],
            results.Select(r => r.Type).Order());
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_SongTheSingerAlreadyHasQueued_NamesTheirOwnTurnAsTheConflict()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var own = Assert.IsType<Performance>(await EnqueueMediaAsync(singerId, mediaId));

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });

        Assert.Same(own, result.Conflict);
    }

    /// <summary>The singer's own copy wins over another's: "you already have it" is the message a
    /// remote should show, not who else does.</summary>
    [Fact]
    public async Task TryCreateAndEnqueueAsync_HeldByTheSingerAndAnother_SaysAlreadyQueued()
    {
        _venue.Settings.RefuseSongQueuedForAnotherSinger = true;
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _performanceDb.Add(new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId, QueuePosition = 1 });
        _performanceDb.Add(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId, QueuePosition = 2 });

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });

        Assert.Equal(EnqueueResultType.AlreadyQueued, result.Type);
        Assert.Equal(singerId, result.Conflict?.SingerId);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_SongAnotherSingerHasQueued_VenueRefusingThem_NamesWhoHasIt()
    {
        _venue.Settings.RefuseSongQueuedForAnotherSinger = true;
        var holder = new KHostUser { Id = Guid.NewGuid(), Name = "Priya" };
        _usersService.ReadAsync(holder.Id).Returns(holder);
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(holder.Id, mediaId);
        var requester = Guid.NewGuid();

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = requester, MediaId = mediaId });

        Assert.Equal(EnqueueResultType.QueuedForAnotherSinger, result.Type);
        Assert.Equal(holder.Id, result.Conflict?.SingerId);
        Assert.Equal("Priya", result.Conflict?.SungAs);
        Assert.Null(result.Performance);
        Assert.Empty((await _service.ReadBySingerIdAsync(requester, filter: PerformanceFilter.Queued)).Items);
    }

    /// <summary>The refusal is the venue's standing answer; a dialog on top of it would ask the host
    /// a question the setting already settled.</summary>
    [Fact]
    public async Task TryCreateAndEnqueueAsync_SongAnotherSingerHasQueued_VenueRefusingThem_DoesNotWarnTheHost()
    {
        _venue.Settings.RefuseSongQueuedForAnotherSinger = true;
        _venue.Settings.WarnOnDuplicateSong = true;
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_SongAnotherSingerHasSung_VenueRefusingQueuedOnes_IsQueued()
    {
        _venue.Settings.RefuseSongQueuedForAnotherSinger = true;
        var mediaId = Guid.NewGuid();
        var sung = Assert.IsType<Performance>(await EnqueueMediaAsync(Guid.NewGuid(), mediaId));
        sung.QueuePosition = null;

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });

        Assert.Equal(EnqueueResultType.Queued, result.Type);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_TwoSingersAtOnce_VenueRefusingTheSecond_QueueItOnce()
    {
        _venue.Settings.RefuseSongQueuedForAnotherSinger = true;
        var mediaId = Guid.NewGuid();
        var gateAsked = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource<PlaybackGateResult>();
        var asked = 0;
        var gates = Substitute.For<IMediaGateService>();
        gates.EvaluateAsync(MediaAction.Queue, Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref asked) == 2)
                gateAsked.SetResult();
            return releaseGate.Task;
        });
        _services.GetService(typeof(IMediaGateService)).Returns(gates);
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/library/song.mp4", Title = "Song" });

        var first = _service.TryCreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });
        var second = _service.TryCreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });
        await gateAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseGate.SetResult(PlaybackGateResult.Ok);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(
            [EnqueueResultType.Queued, EnqueueResultType.QueuedForAnotherSinger],
            results.Select(r => r.Type).Order());
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_RemoteSignupAtTheLimit_IsRefused()
    {
        _venue.Settings.RemoteSongLimit = 2;
        var singerId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, Guid.NewGuid());
        await EnqueueMediaAsync(singerId, Guid.NewGuid());

        var result = await RemoteSignupAsync(singerId);

        Assert.Equal(EnqueueResultType.SingerAtLimit, result.Type);
        Assert.Equal(2, (await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued)).Items.Count);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_RemoteSignupBelowTheLimit_IsQueued()
    {
        _venue.Settings.RemoteSongLimit = 2;
        var singerId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, Guid.NewGuid());

        Assert.Equal(EnqueueResultType.Queued, (await RemoteSignupAsync(singerId)).Type);
    }

    /// <summary>The limit is on guests: the host adding one more for a singer is never refused, and
    /// the overload that names nobody is the host.</summary>
    [Fact]
    public async Task TryCreateAndEnqueueAsync_TheHostAtTheLimit_IsQueued()
    {
        _venue.Settings.RemoteSongLimit = 1;
        var singerId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, Guid.NewGuid());

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });

        Assert.Equal(EnqueueResultType.Queued, result.Type);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_NoLimitSet_TakesEveryRemoteSignup()
    {
        var singerId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await EnqueueMediaAsync(singerId, Guid.NewGuid());

        Assert.Equal(EnqueueResultType.Queued, (await RemoteSignupAsync(singerId)).Type);
    }

    /// <summary>Another singer's songs are theirs: a full room does not fill anyone else's quota.</summary>
    [Fact]
    public async Task TryCreateAndEnqueueAsync_RemoteSignup_CountsOnlyTheSingersOwnSongs()
    {
        _venue.Settings.RemoteSongLimit = 1;
        var other = Guid.NewGuid();
        await EnqueueMediaAsync(other, Guid.NewGuid());
        await EnqueueMediaAsync(other, Guid.NewGuid());

        Assert.Equal(EnqueueResultType.Queued, (await RemoteSignupAsync(Guid.NewGuid())).Type);
    }

    /// <summary>A song already sung is off the queue and no longer counts.</summary>
    [Fact]
    public async Task TryCreateAndEnqueueAsync_RemoteSignup_DoesNotCountSongsAlreadySung()
    {
        _venue.Settings.RemoteSongLimit = 1;
        var singerId = Guid.NewGuid();
        var sung = Assert.IsType<Performance>(await EnqueueMediaAsync(singerId, Guid.NewGuid()));
        sung.QueuePosition = null;

        Assert.Equal(EnqueueResultType.Queued, (await RemoteSignupAsync(singerId)).Type);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_TwoRemoteSignupsAtOnce_LimitOfOne_QueueOne()
    {
        _venue.Settings.RemoteSongLimit = 1;
        var singerId = Guid.NewGuid();
        var gateAsked = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource<PlaybackGateResult>();
        var asked = 0;
        var gates = Substitute.For<IMediaGateService>();
        gates.EvaluateAsync(MediaAction.Queue, Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref asked) == 2)
                gateAsked.SetResult();
            return releaseGate.Task;
        });
        _services.GetService(typeof(IMediaGateService)).Returns(gates);
        _mediaService.ReadAsync(Arg.Any<Guid>()).Returns(call => new Media { Id = (Guid)call[0], FilePath = "/library/song.mp4", Title = "Song" });

        var first = RemoteSignupAsync(singerId);
        var second = RemoteSignupAsync(singerId);
        await gateAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseGate.SetResult(PlaybackGateResult.Ok);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(
            [EnqueueResultType.Queued, EnqueueResultType.SingerAtLimit],
            results.Select(r => r.Type).Order());
    }

    private Task<EnqueueResult> RemoteSignupAsync(Guid singerId)
        => _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() }, EnqueueOrigin.Remote);

    [Fact]
    public async Task TryCreateAndEnqueueAsync_WarningDeclined_SaysSo()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);
        _interactions.RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>()).Returns(false);

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });

        Assert.Equal(EnqueueResultType.DeclinedAtWarning, result.Type);
        Assert.Null(result.Performance);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_MediaItsProviderRefuses_CarriesTheProvidersReason()
    {
        var mediaId = ArrangeGatedMedia(new PlaybackGateResult(false, "Sign in to the provider."));

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId });

        Assert.Equal(EnqueueResultType.RefusedByProvider, result.Type);
        Assert.Equal("Sign in to the provider.", result.Reason);
        Assert.Null(result.Performance);
    }

    // ── songs whose file the host removed ──────────────────────────────────────────────

    private (Media Media, IMediaLifetimeService Lifetime) ArrangeRemovedFile(bool canRefetch)
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/karaoke/youtube/abc.mp4", Title = "Africa", Source = "YouTube", Status = MediaStatus.NotDownloaded, IsEphemeral = true };
        _mediaService.ReadAsync(media.Id).Returns(media);
        var lifetime = Substitute.For<IMediaLifetimeService>();
        lifetime.RefetchAsync(media).Returns(canRefetch);
        _services.GetService(typeof(IMediaLifetimeService)).Returns(lifetime);
        return (media, lifetime);
    }

    /// <summary>Queued behind a download, the same as a fresh pick from its provider.</summary>
    [Fact]
    public async Task TryCreateAndEnqueueAsync_ASongWhoseFileWasRemoved_FetchesItAgainAndQueues()
    {
        var (media, lifetime) = ArrangeRemovedFile(canRefetch: true);

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = media.Id });

        Assert.Equal(EnqueueResultType.Queued, result.Type);
        await lifetime.Received(1).RefetchAsync(media);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_ASongNoInstalledPluginCanFetchAgain_IsRefusedNamingItsSource()
    {
        var (media, _) = ArrangeRemovedFile(canRefetch: false);

        var result = await _service.TryCreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = media.Id });

        Assert.Equal(EnqueueResultType.RefusedByProvider, result.Type);
        Assert.Contains("YouTube cannot do it", result.Reason);
        Assert.Null(result.Performance);
    }

    [Fact]
    public async Task TryCreateAndEnqueueAsync_ASongWithItsFile_IsNotFetchedAgain()
    {
        var (media, lifetime) = ArrangeRemovedFile(canRefetch: true);
        media.Status = MediaStatus.Ready;

        await _service.TryCreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = media.Id });

        await lifetime.DidNotReceive().RefetchAsync(Arg.Any<Media>());
    }

    /// <summary>Asked with what is still queued, so a song another singer picked keeps its file.</summary>
    [Fact]
    public async Task DequeueAsync_AsksForASingleUseFileToGoWithWhatIsStillQueued()
    {
        var lifetime = Substitute.For<IMediaLifetimeService>();
        _services.GetService(typeof(IMediaLifetimeService)).Returns(lifetime);
        var sharedMedia = Guid.NewGuid();
        var first = await EnqueueMediaAsync(Guid.NewGuid(), sharedMedia);
        await EnqueueMediaAsync(Guid.NewGuid(), sharedMedia);

        await _service.DequeueAsync(first!.SingerId, first.Id);

        await lifetime.Received(1).RemoveSingleUseFileIfDoneAsync(
            sharedMedia, Arg.Is<IReadOnlyCollection<Guid>>(queued => queued.Contains(sharedMedia)));
    }

    [Fact]
    public async Task DequeueAsync_TheFileCleanupThrows_StillDequeues()
    {
        var lifetime = Substitute.For<IMediaLifetimeService>();
        lifetime.RemoveSingleUseFileIfDoneAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(Task.FromException(new IOException("busy")));
        _services.GetService(typeof(IMediaLifetimeService)).Returns(lifetime);
        var singerId = Guid.NewGuid();
        var perf = await EnqueueForAsync(singerId);

        await _service.DequeueAsync(singerId, perf.Id);

        Assert.Empty((await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued)).Items);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_SongTheSingerAlreadyHasQueued_IsRefusedWithoutWarningTheHost()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, mediaId);

        await EnqueueMediaAsync(singerId, mediaId);

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_SameSongForADifferentSinger_IsQueued()
    {
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        Assert.NotNull(await EnqueueMediaAsync(Guid.NewGuid(), mediaId));
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_SongTheSingerHasAlreadySung_IsQueued()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var sung = Assert.IsType<Performance>(await EnqueueMediaAsync(singerId, mediaId));
        sung.QueuePosition = null;

        Assert.NotNull(await EnqueueMediaAsync(singerId, mediaId));
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_TwoTapsWhileTheGateIsAsked_QueueOnce()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var gateAsked = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource<PlaybackGateResult>();
        var asked = 0;
        var gates = Substitute.For<IMediaGateService>();
        gates.EvaluateAsync(MediaAction.Queue, Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref asked) == 2)
                gateAsked.SetResult();
            return releaseGate.Task;
        });
        _services.GetService(typeof(IMediaGateService)).Returns(gates);
        _mediaService.ReadAsync(mediaId).Returns(new Media { Id = mediaId, FilePath = "/library/song.mp4", Title = "Song" });
        // A database write yields; a synchronous one would run the two taps one after the other anyway.
        _repository.CreateAsync(Arg.Any<Performance>()).Returns(async args =>
        {
            await Task.Yield();
            var perf = (Performance)args[0];
            lock (_performanceDb)
                _performanceDb.Add(perf);
            return perf;
        });

        var first = EnqueueMediaAsync(singerId, mediaId);
        var second = EnqueueMediaAsync(singerId, mediaId);
        await gateAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseGate.SetResult(PlaybackGateResult.Ok);
        await Task.WhenAll(first, second);

        Assert.Single((await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued)).Items);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_Warns_WhenSongWasSungInsideTheWindow()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        _venue.Settings.DuplicateSongWindowHours = 4;
        var mediaId = Guid.NewGuid();

        _performanceDb.Add(new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = Guid.NewGuid(),
            MediaId = mediaId,
            QueuePosition = null,
            CreatedDate = DateTime.UtcNow.AddHours(-2)
        });

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.Received(1).RequestAsync(
            Arg.Is<ConfirmDuplicateSongRequest>(r => r.SungWithinHours == 2));
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_DoesNotWarn_WhenSongWasSungOutsideTheWindow()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        _venue.Settings.DuplicateSongWindowHours = 4;
        var mediaId = Guid.NewGuid();

        _performanceDb.Add(new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = Guid.NewGuid(),
            MediaId = mediaId,
            QueuePosition = null,
            CreatedDate = DateTime.UtcNow.AddHours(-9)
        });

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    private async Task<Performance> EnqueueForAsync(Guid singerId)
        => Assert.IsType<Performance>(await EnqueueMediaAsync(singerId, Guid.NewGuid()));

    private async Task<Performance?> EnqueueMediaAsync(Guid singerId, Guid mediaId)
        => await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });
    [Fact]
    public async Task CreateAndEnqueueAsync_RecordsTheNameTheSingerHadAtTheTime()
    {
        var singer = new KHostUser { Id = Guid.NewGuid(), Name = "Priya" };
        _usersService.ReadAsync(singer.Id).Returns(singer);

        var enqueued = await _service.CreateAndEnqueueAsync(new Performance
        {
            SingerId = singer.Id,
            MediaId = Guid.NewGuid(),
        });

        // Written here rather than by each caller: there are five, two of them in plugins.
        Assert.Equal("Priya", enqueued!.SungAs);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_ACallerThatBroughtItsOwnName_KeepsIt()
    {
        var singer = new KHostUser { Id = Guid.NewGuid(), Name = "Priya" };
        _usersService.ReadAsync(singer.Id).Returns(singer);

        var enqueued = await _service.CreateAndEnqueueAsync(new Performance
        {
            SingerId = singer.Id,
            MediaId = Guid.NewGuid(),
            SungAs = "DJ P",
        });

        // A nickname typed on a song-first remote is the same person under another name, and the
        // row has to keep the one they typed rather than the one the account carries.
        Assert.Equal("DJ P", enqueued!.SungAs);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_ASingerWhoCannotBeRead_LeavesTheNameUnknownRatherThanFailing()
    {
        _usersService.ReadAsync(Arg.Any<Guid>()).Returns((KHostUser?)null);

        var enqueued = await _service.CreateAndEnqueueAsync(new Performance
        {
            SingerId = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
        });

        Assert.NotNull(enqueued);
        Assert.Null(enqueued.SungAs);
    }

}
