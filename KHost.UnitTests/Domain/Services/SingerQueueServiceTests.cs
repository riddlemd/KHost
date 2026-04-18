using KHost.Abstractions.Services;
using KHost.Domain.Models;
using KHost.Domain.Services;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class SingerQueueServiceTests : IDisposable
{
    private const string _cacheDir = "./cache";
    private readonly IOptionsMonitor<SingerQueueService.ServiceOptions> _options =
        Substitute.For<IOptionsMonitor<SingerQueueService.ServiceOptions>>();
    private readonly ICacheService _cacheService;
    private readonly SingerQueueService _service;

    public SingerQueueServiceTests()
    {
        var cacheOptions = Substitute.For<IOptionsMonitor<JsonFileCacheService.ServiceOptions>>();
        cacheOptions.CurrentValue.Returns(new JsonFileCacheService.ServiceOptions { CachePath = _cacheDir });
        _cacheService = new JsonFileCacheService(cacheOptions);
        _service = new SingerQueueService(_options, _cacheService);
    }

    public void Dispose()
    {
        var cacheFile = Path.Combine(_cacheDir, "singer-queue.json");
        if (File.Exists(cacheFile))
            File.Delete(cacheFile);
    }

    [Fact]
    public void NewService_StartsEmpty()
    {
        Assert.Empty(_service.Singers);
        Assert.Null(_service.SelectedSingerId);
        Assert.Null(_service.SelectedSinger);
        Assert.Null(_service.CurrentlyPerformingSinger);
    }

    [Fact]
    public async Task AddSingerAsync_AddsSingerAndReturnsIt()
    {
        var singer = await _service.AddSingerAsync("Alice");

        Assert.Single(_service.Singers);
        Assert.Equal("Alice", singer.Name);
        Assert.NotEqual(Guid.Empty, singer.Id);
    }

    [Fact]
    public async Task AddSingerAsync_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += () => raised = true;

        await _service.AddSingerAsync("Alice");

        Assert.True(raised);
    }

    [Fact]
    public async Task RemoveSingerAsync_RemovesSinger()
    {
        var alice = await _service.AddSingerAsync("Alice");

        await _service.RemoveSingerAsync(alice.Id);

        Assert.Empty(_service.Singers);
    }

    [Fact]
    public async Task RemoveSingerAsync_ClearsSelection_IfRemovingSelected()
    {
        var alice = await _service.AddSingerAsync("Alice");
        await _service.SelectSingerAsync(alice.Id);

        await _service.RemoveSingerAsync(alice.Id);

        Assert.Null(_service.SelectedSingerId);
    }

    [Fact]
    public async Task SelectSingerAsync_SetsSelectedId_AndClearsSongSelection()
    {
        var alice = await _service.AddSingerAsync("Alice");
        var bob = await _service.AddSingerAsync("Bob");

        await _service.SelectSingerAsync(bob.Id);

        Assert.Equal(bob.Id, _service.SelectedSingerId);
        Assert.Equal("Bob", _service.SelectedSinger!.Name);
        Assert.Null(_service.SelectedQueuedSongId);
    }

    [Fact]
    public async Task AddSongAsync_AddsSongToSingerQueue()
    {
        var alice = await _service.AddSingerAsync("Alice");
        var entity = new SongSearchEntity
        {
            FilePath = "/music/song.mp4",
            DisplayName = "My Song",
            Format = "MP4"
        };

        await _service.AddSongAsync(alice.Id, entity);

        Assert.Single(alice.SongQueue);
        Assert.Equal("My Song", alice.SongQueue[0].Song.Title);
        Assert.Equal("/music/song.mp4", alice.SongQueue[0].FilePath);
    }

    [Fact]
    public async Task AddSongAsync_DoesNothing_ForUnknownSinger()
    {
        var entity = new SongSearchEntity
        {
            FilePath = "/music/song.mp4",
            DisplayName = "My Song",
            Format = "MP4"
        };

        await _service.AddSongAsync(Guid.NewGuid(), entity);

        Assert.Empty(_service.Singers);
    }

    [Fact]
    public async Task RemoveQueuedSongAsync_RemovesSong()
    {
        var alice = await _service.AddSingerAsync("Alice");
        await _service.AddSongAsync(alice.Id, new SongSearchEntity
        {
            FilePath = "/music/a.mp4",
            DisplayName = "A",
            Format = "MP4"
        });
        var queuedSong = alice.SongQueue[0];

        await _service.RemoveQueuedSongAsync(alice.Id, queuedSong.Id);

        Assert.Empty(alice.SongQueue);
    }

    [Fact]
    public async Task MoveSingerUpAsync_SwapsWithPrevious()
    {
        var a = await _service.AddSingerAsync("A");
        var b = await _service.AddSingerAsync("B");

        await _service.MoveSingerUpAsync(b.Id);

        Assert.Equal(b.Id, _service.Singers[0].Id);
        Assert.Equal(a.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerUpAsync_DoesNothing_ForFirst()
    {
        var a = await _service.AddSingerAsync("A");
        var b = await _service.AddSingerAsync("B");

        await _service.MoveSingerUpAsync(a.Id);

        Assert.Equal(a.Id, _service.Singers[0].Id);
        Assert.Equal(b.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerDownAsync_SwapsWithNext()
    {
        var a = await _service.AddSingerAsync("A");
        var b = await _service.AddSingerAsync("B");

        await _service.MoveSingerDownAsync(a.Id);

        Assert.Equal(b.Id, _service.Singers[0].Id);
        Assert.Equal(a.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerDownAsync_DoesNothing_ForLast()
    {
        var a = await _service.AddSingerAsync("A");
        var b = await _service.AddSingerAsync("B");

        await _service.MoveSingerDownAsync(b.Id);

        Assert.Equal(a.Id, _service.Singers[0].Id);
        Assert.Equal(b.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerToStartAsync_MovesToFirst()
    {
        await _service.AddSingerAsync("A");
        await _service.AddSingerAsync("B");
        var c = await _service.AddSingerAsync("C");

        await _service.MoveSingerToStartAsync(c.Id);

        Assert.Equal(c.Id, _service.Singers[0].Id);
        Assert.Equal(3, _service.Singers.Count);
    }

    [Fact]
    public async Task MoveSingerToEndAsync_MovesToLast()
    {
        var a = await _service.AddSingerAsync("A");
        await _service.AddSingerAsync("B");
        await _service.AddSingerAsync("C");

        await _service.MoveSingerToEndAsync(a.Id);

        Assert.Equal(a.Id, _service.Singers[^1].Id);
        Assert.Equal(3, _service.Singers.Count);
    }

    [Fact]
    public async Task MoveSingerUp_SkipsWhen_SingerIsPerforming()
    {
        var a = await _service.AddSingerAsync("A");
        var b = await _service.AddSingerAsync("B");
        b.IsPerforming = true;

        await _service.MoveSingerUpAsync(b.Id);

        Assert.Equal(a.Id, _service.Singers[0].Id);
        Assert.Equal(b.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task ToggleSingerIsRegularAsync_TogglesFlag()
    {
        var alice = await _service.AddSingerAsync("Alice");
        Assert.False(alice.IsRegular);

        await _service.ToggleSingerIsRegularAsync(alice.Id);
        Assert.True(alice.IsRegular);

        await _service.ToggleSingerIsRegularAsync(alice.Id);
        Assert.False(alice.IsRegular);
    }

    [Fact]
    public async Task ToggleSingerIsTipperAsync_TogglesFlag()
    {
        var alice = await _service.AddSingerAsync("Alice");
        Assert.False(alice.IsTipper);

        await _service.ToggleSingerIsTipperAsync(alice.Id);
        Assert.True(alice.IsTipper);
    }

    [Fact]
    public async Task SelectFirstSingerInQueueAsync_DoesNothing_WhenEmpty()
    {
        await _service.SelectFirstSingerInQueueAsync();

        Assert.Null(_service.SelectedSingerId);
    }

    [Fact]
    public async Task SelectFirstSingerInQueueAsync_SelectsFirst()
    {
        var a = await _service.AddSingerAsync("A");
        await _service.AddSingerAsync("B");

        await _service.SelectFirstSingerInQueueAsync();

        Assert.Equal(a.Id, _service.SelectedSingerId);
    }

    [Fact]
    public async Task MoveQueuedSongUpAsync_SwapsWithPrevious()
    {
        var alice = await _service.AddSingerAsync("Alice");
        await _service.AddSongAsync(alice.Id, new SongSearchEntity { FilePath = "/a.mp4", DisplayName = "A", Format = "MP4" });
        await _service.AddSongAsync(alice.Id, new SongSearchEntity { FilePath = "/b.mp4", DisplayName = "B", Format = "MP4" });
        var songA = alice.SongQueue[0];
        var songB = alice.SongQueue[1];

        await _service.MoveQueuedSongUpAsync(alice.Id, songB.Id);

        Assert.Equal(songB.Id, alice.SongQueue[0].Id);
        Assert.Equal(songA.Id, alice.SongQueue[1].Id);
    }

    [Fact]
    public async Task MoveQueuedSongDownAsync_SwapsWithNext()
    {
        var alice = await _service.AddSingerAsync("Alice");
        await _service.AddSongAsync(alice.Id, new SongSearchEntity { FilePath = "/a.mp4", DisplayName = "A", Format = "MP4" });
        await _service.AddSongAsync(alice.Id, new SongSearchEntity { FilePath = "/b.mp4", DisplayName = "B", Format = "MP4" });
        var songA = alice.SongQueue[0];
        var songB = alice.SongQueue[1];

        await _service.MoveQueuedSongDownAsync(alice.Id, songA.Id);

        Assert.Equal(songB.Id, alice.SongQueue[0].Id);
        Assert.Equal(songA.Id, alice.SongQueue[1].Id);
    }

    [Fact]
    public async Task MoveQueuedSongToEndAsync_MovesToEnd()
    {
        var alice = await _service.AddSingerAsync("Alice");
        await _service.AddSongAsync(alice.Id, new SongSearchEntity { FilePath = "/a.mp4", DisplayName = "A", Format = "MP4" });
        await _service.AddSongAsync(alice.Id, new SongSearchEntity { FilePath = "/b.mp4", DisplayName = "B", Format = "MP4" });
        await _service.AddSongAsync(alice.Id, new SongSearchEntity { FilePath = "/c.mp4", DisplayName = "C", Format = "MP4" });
        var first = alice.SongQueue[0];

        await _service.MoveQueuedSongToEndAsync(alice.Id, first.Id);

        Assert.Equal(first.Id, alice.SongQueue[^1].Id);
    }

    [Fact]
    public async Task CurrentlyPerformingSinger_ReturnsPerformingSinger()
    {
        var a = await _service.AddSingerAsync("A");
        var b = await _service.AddSingerAsync("B");
        b.IsPerforming = true;

        Assert.Equal(b.Id, _service.CurrentlyPerformingSinger!.Id);
    }
}
