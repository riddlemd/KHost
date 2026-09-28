using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>ffmpeg's last playlist rename can fail on Windows while a scanner holds the file; the
/// host then moves the .tmp it left into place itself.</summary>
public sealed class HlsMediaStreamServiceCompletePlaylistTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("khost-complete-playlist-").FullName;

    private string Playlist => Path.Combine(_directory, "stream.m3u8");
    private string Pending => Playlist + ".tmp";

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task CompletePlaylistAsync_ATmpWasLeft_MovesItOverThePlaylist()
    {
        File.WriteAllText(Playlist, "#EXTM3U\nseg_00000.ts\n");
        File.WriteAllText(Pending, "#EXTM3U\nseg_00000.ts\nseg_00001.ts\n#EXT-X-ENDLIST\n");

        Assert.True(await HlsMediaStreamService.CompletePlaylistAsync(_directory));

        Assert.False(File.Exists(Pending));
        Assert.Contains("#EXT-X-ENDLIST", File.ReadAllText(Playlist));
    }

    [Fact]
    public async Task CompletePlaylistAsync_NoTmp_LeavesThePlaylistAlone()
    {
        File.WriteAllText(Playlist, "#EXTM3U\nseg_00000.ts\n#EXT-X-ENDLIST\n");

        Assert.False(await HlsMediaStreamService.CompletePlaylistAsync(_directory));

        Assert.Equal("#EXTM3U\nseg_00000.ts\n#EXT-X-ENDLIST\n", File.ReadAllText(Playlist));
    }

    // Windows is where a held file blocks the move; elsewhere the first attempt succeeds anyway.
    [Fact]
    public async Task CompletePlaylistAsync_ThePlaylistIsHeldForAMoment_RetriesUntilItLetsGo()
    {
        File.WriteAllText(Playlist, "#EXTM3U\nseg_00000.ts\n");
        File.WriteAllText(Pending, "#EXTM3U\nseg_00000.ts\n#EXT-X-ENDLIST\n");

        var holder = new FileStream(Playlist, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Delay(300).ContinueWith(_ => holder.Dispose(), TaskScheduler.Default);

        var moved = await HlsMediaStreamService.CompletePlaylistAsync(_directory, attempts: 20, retryMilliseconds: 50);
        await release;

        Assert.True(moved);
        Assert.Contains("#EXT-X-ENDLIST", File.ReadAllText(Playlist));
    }
}
