using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.MediaLifetime;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services.MediaLifetime;

public class MediaLifetimeServiceTests : IDisposable
{
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly DownloadsService _downloads = new(new MessageBroker(NullLogger<MessageBroker>.Instance));
    private readonly MediaAcquisitionService _acquisition;
    private readonly List<IMediaRefetcher> _refetchers = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-lifetime-{Guid.NewGuid():N}");

    public MediaLifetimeServiceTests()
    {
        Directory.CreateDirectory(_directory);
        var options = Substitute.For<IOptionsMonitor<MediaAcquisitionService.ServiceOptions>>();
        options.CurrentValue.Returns(new MediaAcquisitionService.ServiceOptions());
        _acquisition = new MediaAcquisitionService(
            NullLogger<MediaAcquisitionService>.Instance, _repository, _mediaService, options, _downloads,
            new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private MediaLifetimeService Service() => new(
        NullLogger<MediaLifetimeService>.Instance, _repository, _mediaService, _acquisition, _refetchers);

    private Media Row(MediaStatus status, bool file, bool ephemeral = false, bool singleUse = false)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.mp4");
        if (file) File.WriteAllBytes(path, [1]);

        var media = new Media { Id = Guid.NewGuid(), FilePath = path, Title = "Africa", Source = "YouTube", Status = status, IsEphemeral = ephemeral, IsSingleUse = singleUse };
        _mediaService.ReadAsync(media.Id).Returns(media);
        return media;
    }

    private IMediaRefetcher Refetcher(bool claims, Func<Media, ImportTicket, Task>? run = null)
    {
        var refetcher = Substitute.For<IMediaRefetcher>();
        refetcher.CanRefetch(Arg.Any<Media>()).Returns(claims);
        refetcher.RefetchAsync(Arg.Any<Media>(), Arg.Any<ImportTicket>())
            .Returns(call => run?.Invoke(call.Arg<Media>(), call.Arg<ImportTicket>()) ?? Task.CompletedTask);
        _refetchers.Add(refetcher);
        return refetcher;
    }

    [Fact]
    public async Task RefetchAsync_AProviderCanFetchIt_PutsTheRowInFlightAndHandsItTheTicket()
    {
        var media = Row(MediaStatus.NotDownloaded, file: false, ephemeral: true);
        var ran = new TaskCompletionSource<ImportTicket>();
        Refetcher(claims: true, run: (_, ticket) => { ran.SetResult(ticket); return Task.CompletedTask; });

        Assert.True(await Service().RefetchAsync(media));

        Assert.Equal(MediaStatus.Downloading, media.Status);
        var ticket = await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(media.Id, ticket.MediaId);
    }

    [Fact]
    public async Task RefetchAsync_NoProviderCanFetchIt_LeavesTheRowWaiting()
    {
        var media = Row(MediaStatus.NotDownloaded, file: false, ephemeral: true);
        var refetcher = Refetcher(claims: false);

        Assert.False(await Service().RefetchAsync(media));

        Assert.Equal(MediaStatus.NotDownloaded, media.Status);
        await refetcher.DidNotReceive().RefetchAsync(Arg.Any<Media>(), Arg.Any<ImportTicket>());
    }

    [Theory]
    [InlineData(MediaStatus.Ready)]
    [InlineData(MediaStatus.Downloading)]
    public async Task RefetchAsync_ARowNotWaitingForItsFile_FetchesNothing(MediaStatus status)
    {
        var media = Row(status, file: false, ephemeral: true);
        var refetcher = Refetcher(claims: true);

        Assert.False(await Service().RefetchAsync(media));

        await refetcher.DidNotReceive().RefetchAsync(Arg.Any<Media>(), Arg.Any<ImportTicket>());
    }

    /// <summary>One plugin throwing while deciding must not take the others' answer away.</summary>
    [Fact]
    public async Task RefetchAsync_AProviderThatThrowsDeciding_IsSkipped()
    {
        var media = Row(MediaStatus.NotDownloaded, file: false, ephemeral: true);
        var broken = Substitute.For<IMediaRefetcher>();
        broken.CanRefetch(Arg.Any<Media>()).Returns(_ => throw new InvalidOperationException("boom"));
        _refetchers.Add(broken);
        var working = Refetcher(claims: true);

        Assert.True(await Service().RefetchAsync(media));

        // The refetch runs in the background, so it is waited for rather than raced.
        await WaitUntilAsync(() => working.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IMediaRefetcher.RefetchAsync)));
    }

    /// <summary>A provider that throws instead of settling would otherwise leave the row Downloading for good.</summary>
    [Fact]
    public async Task RefetchAsync_TheProviderThrows_SettlesTheRowBackToWaiting()
    {
        var media = Row(MediaStatus.NotDownloaded, file: false, ephemeral: true);
        Refetcher(claims: true, run: (_, _) => throw new InvalidOperationException("network"));

        await Service().RefetchAsync(media);

        await WaitUntilAsync(() => media.Status == MediaStatus.NotDownloaded);
    }

    [Fact]
    public async Task RemoveFilesOnCloseAsync_DeletesEachEphemeralAndSingleUseFile_AndKeepsTheRowsWaiting()
    {
        var ephemeral = Row(MediaStatus.Ready, file: true, ephemeral: true);
        var brokenEphemeral = Row(MediaStatus.Broken, file: true, ephemeral: true);
        var singleUse = Row(MediaStatus.Ready, file: true, singleUse: true);
        _repository.ReadWithFileLifetimeAsync().Returns([ephemeral, brokenEphemeral, singleUse]);

        await Service().RemoveFilesOnCloseAsync([]);

        Assert.False(File.Exists(ephemeral.FilePath));
        Assert.False(File.Exists(brokenEphemeral.FilePath));
        Assert.False(File.Exists(singleUse.FilePath));
        Assert.Equal(MediaStatus.NotDownloaded, ephemeral.Status);
        Assert.Equal(MediaStatus.NotDownloaded, brokenEphemeral.Status);
        Assert.Equal(MediaStatus.NotDownloaded, singleUse.Status);
        await _mediaService.Received(1).UpdateAsync(ephemeral);
        await _mediaService.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    /// <summary>A row still arriving is the download's to settle.</summary>
    [Fact]
    public async Task RemoveFilesOnCloseAsync_LeavesARowStillArrivingAlone()
    {
        var arriving = Row(MediaStatus.Downloading, file: true, ephemeral: true);
        _repository.ReadWithFileLifetimeAsync().Returns([arriving]);

        await Service().RemoveFilesOnCloseAsync([]);

        Assert.True(File.Exists(arriving.FilePath));
        Assert.Equal(MediaStatus.Downloading, arriving.Status);
    }

    /// <summary>A venue that keeps its queue carries the turn into the next session, where it is played
    /// rather than queued, so nothing would fetch the file again.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RemoveFilesOnCloseAsync_KeepsTheFileOfASongStillQueued(bool ephemeral, bool singleUse)
    {
        var queued = Row(MediaStatus.Ready, file: true, ephemeral: ephemeral, singleUse: singleUse);
        _repository.ReadWithFileLifetimeAsync().Returns([queued]);

        await Service().RemoveFilesOnCloseAsync([queued.Id]);

        Assert.True(File.Exists(queued.FilePath));
        Assert.Equal(MediaStatus.Ready, queued.Status);
    }

    [Fact]
    public async Task RemoveSingleUseFileIfDoneAsync_SungAndNotQueued_DeletesTheFile()
    {
        var media = Row(MediaStatus.Ready, file: true, singleUse: true);

        await Service().RemoveSingleUseFileIfDoneAsync(media.Id, []);

        Assert.False(File.Exists(media.FilePath));
        Assert.Equal(MediaStatus.NotDownloaded, media.Status);
    }

    /// <summary>Two singers picked the same song: the second turn still needs the file.</summary>
    [Fact]
    public async Task RemoveSingleUseFileIfDoneAsync_AnotherTurnStillWaits_KeepsTheFile()
    {
        var media = Row(MediaStatus.Ready, file: true, singleUse: true);

        await Service().RemoveSingleUseFileIfDoneAsync(media.Id, [media.Id]);

        Assert.True(File.Exists(media.FilePath));
        Assert.Equal(MediaStatus.Ready, media.Status);
    }

    [Fact]
    public async Task RemoveSingleUseFileIfDoneAsync_ARowNotMarkedSingleUse_KeepsTheFile()
    {
        var media = Row(MediaStatus.Ready, file: true, ephemeral: true);

        await Service().RemoveSingleUseFileIfDoneAsync(media.Id, []);

        Assert.True(File.Exists(media.FilePath));
        Assert.Equal(MediaStatus.Ready, media.Status);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
