using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;
using Microsoft.Extensions.Logging.Abstractions;
using StemInput = KHost.Domain.Services.HlsMediaStreamService.StemInput;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A song that arrived as separate stems: each one an input at its own level, keyed and
/// retimed as one mix, with a picture only when words are burned in over it.</summary>
public class HlsMediaStreamServiceStemTests : IDisposable
{
    private static readonly IReadOnlyList<StemInput> Stems =
    [
        new("/s/music.ogg", AudioTrackRole.Music, 100),
        new("/s/lead.ogg", AudioTrackRole.Lead, 0),
        new("/s/backing.ogg", AudioTrackRole.Backing, 50),
    ];

    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-stem-unit-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceStemTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251",
                WorkingDirectory = _workingDirectory,
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []));

    [Fact]
    public void BuildStemArguments_MixesEveryStemAtItsOwnLevel_AsAudioAlone()
    {
        var arguments = HlsMediaStreamService.BuildStemArguments(Stems, TimeSpan.Zero, 0, 0, 2);

        Assert.StartsWith(
            "-hide_banner -loglevel error -i \"/s/music.ogg\" -i \"/s/lead.ogg\" -i \"/s/backing.ogg\" ", arguments);
        Assert.Contains(
            " -filter_complex \"[0:a:0]volume=1.000[m0];[1:a:0]volume=0.000[l1];[2:a:0]volume=0.500[b2];"
            + "[m0][l1][b2]amix=inputs=3:normalize=0[x];[x]anull[a]\" -map \"[a]\" ",
            arguments);
        Assert.DoesNotContain("-c:v", arguments);
        Assert.EndsWith(" stream.m3u8", arguments);
    }

    [Fact]
    public void BuildStemArguments_SeeksEveryStemToThePlayhead()
    {
        var arguments = HlsMediaStreamService.BuildStemArguments(Stems, TimeSpan.FromSeconds(42.5), 0, 0, 2);

        Assert.Contains(
            " -ss 42.500 -i \"/s/music.ogg\" -ss 42.500 -i \"/s/lead.ogg\" -ss 42.500 -i \"/s/backing.ogg\"", arguments);
    }

    [Fact]
    public void BuildStemArguments_KeysAndRetimesTheMixedResult()
    {
        var arguments = HlsMediaStreamService.BuildStemArguments(Stems, TimeSpan.Zero, 1, 10, 2);

        Assert.Contains(
            "amix=inputs=3:normalize=0[x];[x]aresample=44100,asetrate=44100*1.059463,aresample=44100,atempo=1.038262[a]\"",
            arguments);
    }

    [Fact]
    public void BuildStemArguments_BurnsWordsOverBlack_BehindEveryStem()
    {
        var overlay = new BurnInOverlay(1280, 720, 30, BurnInBase.Fill);

        var arguments = HlsMediaStreamService.BuildStemArguments(Stems, TimeSpan.Zero, 0, 0, 2, overlay);

        Assert.Contains(
            "-i \"/s/backing.ogg\" -f rawvideo -pix_fmt rgba -s 1280x720 -r 30 -thread_queue_size 64 -i pipe:0"
            + " -f lavfi -i color=c=black:s=1280x720:r=30",
            arguments);
        Assert.Contains(" -c:v libx264 ", arguments);
        Assert.Contains(
            " -filter_complex \"[4:v]setsar=1[base];[base][3:v]overlay=0:0:shortest=1[v];[0:a:0]volume=1.000[m0];",
            arguments);
        Assert.Contains("[x]anull[a]\" -map \"[v]\" -map \"[a]\"", arguments);
    }

    [Fact]
    public void BuildStemArguments_BurnsWordsOverTheVenuesBackground()
    {
        var overlay = new BurnInOverlay(1280, 720, 30, BurnInBase.Background, "/bg/clip.mp4");

        var arguments = HlsMediaStreamService.BuildStemArguments(Stems, TimeSpan.Zero, 0, 0, 2, overlay);

        Assert.Contains("-i pipe:0 -stream_loop -1 -i \"/bg/clip.mp4\"", arguments);
        Assert.Contains(
            "\"[4:v]scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,setsar=1,fps=30[base];"
            + "[base][3:v]overlay=0:0:shortest=1[v];",
            arguments);
    }

    [Fact]
    public async Task ResolveStemInput_ASessionFile_IsReadOffDisk()
    {
        var session = await OpenStemSessionAsync("lead.ogg");

        var input = _service.ResolveStemInput(_service.BuildArtifactUrl(session.Id, "lead.ogg"));

        Assert.Equal(Path.Combine(session.WorkingDirectory!, "lead.ogg"), input);
    }

    [Fact]
    public void ResolveStemInput_AnAddressElsewhere_IsFetchedAsItIs()
        => Assert.Equal("https://cdn.example/lead.ogg", _service.ResolveStemInput("https://cdn.example/lead.ogg"));

    [Theory]
    [InlineData("http://host:5251/media/nosuchsession/lead.ogg")]
    [InlineData("/somewhere/lead.ogg")]
    [InlineData("https://cdn.example/a\"b.ogg")]
    public void ResolveStemInput_AnythingElse_Throws(string url)
        => Assert.Throws<InvalidOperationException>(() => _service.ResolveStemInput(url));

    [Fact]
    public async Task OpenStemsAsync_WhenAStemCannotBeRead_ClosesTheSessionItWasHanded()
    {
        var session = await OpenStemSessionAsync("lead.ogg");
        StemSource[] stems = [new(0, AudioTrackRole.Lead, "http://host:5251/media/nosuchsession/x.ogg", 100)];

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.OpenStemsAsync("/songs/a.song", stems, TimeSpan.Zero, 0, 0, null, null, session));

        Assert.False(Directory.Exists(session.WorkingDirectory));
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
        GC.SuppressFinalize(this);
    }

    private async Task<MediaStreamSession> OpenStemSessionAsync(string stemName)
    {
        Directory.CreateDirectory(_workingDirectory);
        var anchor = Path.Combine(_workingDirectory, "song.song");
        await File.WriteAllTextAsync(anchor, "");

        var session = await _service.OpenWithoutEncodeAsync(anchor);
        await File.WriteAllTextAsync(Path.Combine(session.WorkingDirectory!, stemName), "");

        return session;
    }
}
