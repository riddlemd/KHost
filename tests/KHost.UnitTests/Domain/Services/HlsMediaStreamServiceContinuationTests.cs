using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;
using KHost.Domain.Services.VideoEncoding;
using PlaylistPart = KHost.Domain.Services.HlsMediaStreamService.PlaylistPart;
using StemInput = KHost.Domain.Services.HlsMediaStreamService.StemInput;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A hardware encode leaves its playlist open, so a run that dies mid-song can be carried
/// on in libx264 in the playlist a display is already playing.</summary>
public sealed class HlsMediaStreamServiceContinuationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("khost-continuation-").FullName;

    private string Playlist => Path.Combine(_directory, "stream.m3u8");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    /// <summary>Software songs are cut exactly as before: ffmpeg writes its own end marker.</summary>
    [Fact]
    public void BuildArguments_WholePlaylist_NeitherLeavesItOpenNorAppends()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

        Assert.Contains(" -hls_flags independent_segments -hls_segment_filename", arguments);
        Assert.DoesNotContain("-output_ts_offset", arguments);
    }

    [Fact]
    public void BuildArguments_LeftOpen_LeavesTheEndMarkerToTheHost()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, encoder: VideoEncoderProfile.VideoToolbox,
            part: new PlaylistPart(TimeSpan.Zero, LeftOpen: true));

        Assert.Contains(" -hls_flags independent_segments+omit_endlist -hls_segment_filename", arguments);
        Assert.DoesNotContain("-output_ts_offset", arguments);
    }

    /// <summary>Numbered on from the listed segments, with timestamps that follow theirs.</summary>
    [Fact]
    public void BuildArguments_Appending_ContinuesThePlaylistAndItsClock()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.FromSeconds(41), 0, 0, 2,
            burnIn: new BurnInOverlay(1280, 720, 30, BurnInBase.SourceVideo),
            part: new PlaylistPart(TimeSpan.FromSeconds(36), LeftOpen: false));

        Assert.Contains(" -output_ts_offset 36.000000 -f hls", arguments);
        Assert.Contains(" -hls_flags independent_segments+append_list -hls_segment_filename", arguments);
        Assert.StartsWith("-hide_banner -loglevel error -ss 41.000", arguments);
    }

    [Fact]
    public void BuildStemArguments_Appending_ContinuesThePlaylistAndItsClock()
    {
        var arguments = HlsMediaStreamService.BuildStemArguments(
            [new StemInput("/s/music.ogg", AudioTrackRole.Music, 100)], TimeSpan.FromSeconds(12), 0, 0, 2,
            new BurnInOverlay(1280, 720, 30, BurnInBase.Fill),
            part: new PlaylistPart(TimeSpan.FromSeconds(12), LeftOpen: false));

        Assert.Contains(" -output_ts_offset 12.000000 -f hls", arguments);
        Assert.Contains("+append_list", arguments);
    }

    /// <summary>Listed output runs at the tempo against the song, so the run picks up that much further in.</summary>
    [Theory]
    [InlineData(0, 46)]
    [InlineData(-25, 37)]
    [InlineData(10, 49.6)]
    public void SongTimeAfter_ListedOutput_IsSongTimeAtTheTempo(int tempo, double expected)
        => Assert.Equal(
            expected,
            HlsMediaStreamService.SongTimeAfter(TimeSpan.FromSeconds(10), tempo)(TimeSpan.FromSeconds(36)).TotalSeconds,
            precision: 6);

    [Fact]
    public void ListedSegmentSeconds_ReadsEachSegmentsLength()
        => Assert.Equal(
            [2.0, 2.0, 1.25],
            HlsMediaStreamService.ListedSegmentSeconds(
                "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2.000000,\nseg_00000.ts\n#EXTINF:2.000000,\r\nseg_00001.ts\n"
                + "#EXT-X-DISCONTINUITY\n#EXTINF:1.250000,\nseg_00002.ts\n"));

    [Fact]
    public async Task EndPlaylistAsync_LeftOpen_AddsTheEndMarkerAfterTheLastSegment()
    {
        await File.WriteAllTextAsync(Playlist, "#EXTM3U\n#EXTINF:2.000000,\nseg_00000.ts\n");

        Assert.True(await HlsMediaStreamService.EndPlaylistAsync(_directory));

        Assert.Equal("#EXTM3U\n#EXTINF:2.000000,\nseg_00000.ts\n#EXT-X-ENDLIST\n", await File.ReadAllTextAsync(Playlist));
        Assert.Equal([Playlist], Directory.GetFiles(_directory));
    }

    /// <summary>A second end marker would be a malformed playlist.</summary>
    [Fact]
    public async Task EndPlaylistAsync_AlreadyEnded_LeavesItAlone()
    {
        await File.WriteAllTextAsync(Playlist, "#EXTM3U\nseg_00000.ts\n#EXT-X-ENDLIST\n");

        Assert.False(await HlsMediaStreamService.EndPlaylistAsync(_directory));

        Assert.Equal("#EXTM3U\nseg_00000.ts\n#EXT-X-ENDLIST\n", await File.ReadAllTextAsync(Playlist));
    }
}
