using System.Globalization;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Common.Media;

namespace KHost.UnitTests.Domain.Services;

public class HlsMediaStreamServiceTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-stream-tests-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251",
                WorkingDirectory = _workingDirectory,
            }),
            // The real router with nothing registered: every path resolves to itself, which is
            // what the host does for all but a provider's own container.
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []));

    [Fact]
    public void BuildArguments_TargetsCodecsEveryConsumerDecodes()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

        // The intersection of Chromecast, WKWebView and browser support. Widening any of these
        // silently drops one class of consumer.
        Assert.Contains("-c:v libx264", arguments);
        Assert.Contains("-profile:v main", arguments);
        Assert.Contains("-level 4.1", arguments);
        Assert.Contains("-c:a aac", arguments);
        Assert.Contains("-ar 44100", arguments);
    }

    [Fact]
    public void BuildArguments_SegmentsAsMpegTs_NotFragmentedMp4()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

        // CMAF/fMP4 needs a newer player device than TS does.
        Assert.Contains("-f hls", arguments);
        Assert.Contains("seg_%05d.ts", arguments);
    }

    [Fact]
    public void BuildArguments_PutsSeekBeforeTheInput()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.FromSeconds(42), 0, 0, 2);

        // Input-side -ss is the fast one; after -i ffmpeg decodes everything it skips.
        Assert.True(arguments.IndexOf("-ss 42.000", StringComparison.Ordinal)
                    < arguments.IndexOf("-i ", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildArguments_OmitsTheSeek_WhenStartingAtZero()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

        Assert.DoesNotContain("-ss ", arguments);
    }

    [Fact]
    public void BuildArguments_PairsGraphicsWithTheirCompanionAudio()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3");

        Assert.Contains("-i \"/songs/a.cdg\"", arguments);
        Assert.Contains("-i \"/songs/a.mp3\"", arguments);

        // Without the mapping ffmpeg takes both streams from the first input, which has no audio.
        Assert.Contains("-map 0:v:0 -map 1:a:0", arguments);
    }

    /// <summary>A .cdg emits a frame only when the graphics change, so x264 is handed a wildly
    /// variable rate and encodes far more than the picture needs.</summary>
    /// <remarks>Measured on two songs: 110 and 154 CPU-seconds without this against 33 and 44 with
    /// it, for the same segments either way. The renderer has always done this; streaming did not,
    /// which made playing a CDG without a render cost three times what it had to.</remarks>
    [Fact]
    public void BuildArguments_GivesGraphicsAConstantFrameRate()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3");

        Assert.Contains("-r 30", arguments, StringComparison.Ordinal);
    }

    /// <summary>An ordinary video already has a frame rate; forcing one would resample it.</summary>
    [Fact]
    public void BuildArguments_LeavesAnOrdinaryVideosFrameRateAlone()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

        Assert.DoesNotContain("-r 30", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArguments_SeeksOnTheOutput_ForAPairedSource()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.FromSeconds(42), 0, 0, 2, "/songs/a.mp3");

        // An input seek lands mid-packet and CDG decodes to garbage from there.
        var seek = arguments.IndexOf("-ss 42.000", StringComparison.Ordinal);
        var lastInput = arguments.LastIndexOf("-i \"", StringComparison.Ordinal);
        Assert.True(seek > lastInput, $"seek must follow both inputs: {arguments}");
    }

    [Fact]
    public void BuildArguments_SeeksOnTheOutput_ForGraphicsWithNoAudioBesideThem()
    {
        // The stateful decode is what forces the slow seek, not the pairing. CompactDiscPlusGraphicsRenderer
        // refuses this pairing before the encoder ever sees it, so this is defence in depth — but
        // the predicate has to be about the decode, or it is right only by coincidence.
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.FromSeconds(42), 0, 0, 2);

        var seek = arguments.IndexOf("-ss 42.000", StringComparison.Ordinal);
        var lastInput = arguments.LastIndexOf("-i \"", StringComparison.Ordinal);
        Assert.True(seek > lastInput, $"seek must follow the input: {arguments}");
    }

    [Fact]
    public void BuildArguments_KeepsTheFastInputSeek_ForAnOrdinaryFile()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.FromSeconds(42), 0, 0, 2);

        Assert.True(
            arguments.IndexOf("-ss 42.000", StringComparison.Ordinal)
                < arguments.IndexOf("-i \"", StringComparison.Ordinal),
            arguments);
    }

    [Fact]
    public void BuildArguments_AddsAPitchFilter_OnlyWhenShifted()
    {
        Assert.DoesNotContain("asetrate", HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2));
        Assert.Contains("asetrate", HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 2, 0, 2));
    }

    [Fact]
    public void BuildArguments_ForcesKeyframesOnTime_NotOnAFrameCount()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

        // -g is frames, so it equals the segment length at one source frame rate only, and the
        // muxer cuts only where a keyframe already is.
        Assert.Contains("-force_key_frames \"expr:gte(t,n_forced*2)\"", arguments);
        Assert.DoesNotContain("-g 60", arguments);
        Assert.DoesNotContain("-keyint_min", arguments);
    }

    [Fact]
    public void BuildArguments_TiesForcedKeyframesToTheSegmentLength()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 4);

        // A keyframe interval disagreeing with hls_time gives ragged segments.
        Assert.Contains("-force_key_frames \"expr:gte(t,n_forced*4)\"", arguments);
        Assert.Contains("-hls_time 4", arguments);
    }

    [Fact]
    public void BuildArguments_ResamplesBeforeReinterpretingTheSampleRate()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 2, 0, 2);

        // asetrate reinterprets whatever rate reaches it, and the atempo below compensates only
        // for the intended ratio, so a 48kHz source drifts off the video without this.
        Assert.Contains("aresample=44100,asetrate=", arguments);
        Assert.True(
            arguments.IndexOf("aresample=44100,asetrate=", StringComparison.Ordinal)
                < arguments.IndexOf("atempo=", StringComparison.Ordinal),
            arguments);
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(6)]
    public void BuildArguments_KeepsTheCompensatingTempoWithinWhatAtempoAccepts(int semitones)
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, semitones, 0, 2);

        var start = arguments.IndexOf("atempo=", StringComparison.Ordinal) + "atempo=".Length;
        var end = arguments.IndexOf('"', start);
        var tempo = double.Parse(arguments[start..end], CultureInfo.InvariantCulture);

        // atempo rejects anything under 0.5, and chaining is the only way past it. Across the
        // supported range pitch alone never gets there; combining it with a tempo change would.
        Assert.InRange(tempo, 0.5, 100.0);
    }

    [Fact]
    public void BuildArguments_RetimesThePicture_WhenTempoChanges()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 25, 2);

        // -af only touches audio, so without this the picture runs at its own speed.
        Assert.Contains("-vf \"setpts=PTS/1.250000\"", arguments);
    }

    [Fact]
    public void BuildArguments_LeavesThePictureAlone_AtTheRecordedTempo()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 6, 0, 2);

        // A pitch shift is audio only; retiming the video for it would desync the lyrics.
        Assert.DoesNotContain("-vf", arguments);
        Assert.DoesNotContain("setpts", arguments);
    }

    [Fact]
    public void BuildArguments_ChainsAtempo_WhereOneStageWouldFallBelowItsFloor()
    {
        // Pitch up against tempo down is the corner: 0.5 / 2^(6/12) is 0.354, which ffmpeg
        // rejects outright rather than clamping.
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 6, -50, 2);

        Assert.Equal(2, CountOccurrences(arguments, "atempo="));
        Assert.All(AtempoFactors(arguments), f => Assert.InRange(f, 0.5, 100.0));
    }

    [Fact]
    public void BuildArguments_UsesASingleAtempoStage_WhereItFits()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, -50, 2);

        Assert.Equal(1, CountOccurrences(arguments, "atempo="));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(0, -50)]
    [InlineData(6, -50)]
    [InlineData(-6, 50)]
    [InlineData(6, 50)]
    [InlineData(-6, -50)]
    [InlineData(3, -20)]
    [InlineData(-2, 15)]
    public void BuildArguments_ComposesPitchAndTempoIntoTheRequestedRate(int pitch, int tempo)
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, pitch, tempo, 2);

        // asetrate speeds audio up by the pitch ratio as a side effect, so the atempo stages carry
        // both the undo and the wanted tempo. Get their product wrong and it drifts, not fails.
        var speed = AsetrateRatio(arguments) * AtempoFactors(arguments).Aggregate(1.0, (a, f) => a * f);

        Assert.Equal(StreamRate.FromTempo(tempo), speed, 4);
    }

    [Theory]
    [InlineData(6, -50)]
    [InlineData(-6, 50)]
    [InlineData(4, 25)]
    public void BuildArguments_ShiftsPitchByTheSemitoneRatio_WhateverTheTempo(int pitch, int tempo)
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, pitch, tempo, 2);

        // The perceived key is asetrate's alone: atempo restores length without touching it.
        Assert.Equal(Math.Pow(2.0, pitch / 12.0), AsetrateRatio(arguments), 4);
    }

    [Fact]
    public void BuildArguments_BalancesTheVoicesOverTheMusic()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null, ThreeTrackMix(lead: 40, backing: 80));

        // The music is the reference the voices are set against, so it is never anything but full.
        Assert.Contains("[0:a:0]volume=1.000[m0]", arguments);
        Assert.Contains("[0:a:2]volume=0.400[l2]", arguments);
        Assert.Contains("[0:a:1]volume=0.800[b1]", arguments);
    }

    [Fact]
    public void BuildArguments_KeepsAmixFromNormalising()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null, ThreeTrackMix(100, 100));

        // Left to normalise, amix divides by the input count and drops the whole mix several dB.
        Assert.Contains("amix=inputs=3:normalize=0", arguments);
    }

    [Fact]
    public void BuildArguments_MapsTheMixedResult_NotARawTrack()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null, ThreeTrackMix(0, 100));

        // Without saying which streams to take, ffmpeg carries a raw track through beside the mix.
        Assert.Contains("-map 0:v:0? -map \"[a]\"", arguments);
        Assert.DoesNotContain("-af", arguments);
    }

    [Fact]
    public void BuildArguments_MapsThePictureOptionally_SoAnAudioOnlyMixStillOpens()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.kfa", TimeSpan.Zero, 0, 0, 2, null, ThreeTrackMix(50, 50));

        // A stems-only container has no video stream, and a required mapping onto one is fatal:
        // ffmpeg exits before a segment is written, which reads as the song simply never starting.
        Assert.Contains("-map 0:v:0?", arguments);
        Assert.DoesNotContain("-map 0:v:0 ", arguments);
    }

    [Fact]
    public void BuildArguments_RidesPitchAndTempoOnTheMix_NotOnOneTrack()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 2, -10, 2, null, ThreeTrackMix(50, 50));

        // The transposition belongs to the song, so it has to come after the voices are combined.
        var mix = arguments.IndexOf("amix=", StringComparison.Ordinal);
        var pitch = arguments.IndexOf("asetrate=", StringComparison.Ordinal);

        Assert.True(mix < pitch, arguments);
        Assert.Contains("[x]aresample=", arguments);
    }

    [Fact]
    public void BuildArguments_LeavesASingleTrackFileOnTheSimplePath()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 2, 0, 2, null,
            new AudioMix([new AudioTrack(0, AudioTrackRole.Music, "Instrumental")], 0, 100));

        // One track is nothing to balance; building a graph for it would only add ways to fail.
        Assert.DoesNotContain("filter_complex", arguments);
        Assert.Contains("-af", arguments);
    }

    [Fact]
    public void BuildArguments_MixesWhatTheFileHas_WhenThereIsNoBackingTrack()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null,
            new AudioMix(
            [
                new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
                new AudioTrack(1, AudioTrackRole.Lead, "Lead Vocal"),
            ], LeadVolume: 30, BackingVolume: 100));

        Assert.Contains("amix=inputs=2:normalize=0", arguments);
        Assert.Contains("[0:a:1]volume=0.300[l1]", arguments);
    }

    [Theory]
    [InlineData(-40, "0.000")]
    [InlineData(180, "1.000")]
    public void BuildArguments_ClampsAVolumeToWhatAFaderCanAsk(int lead, string expected)
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null, ThreeTrackMix(lead, 100));

        Assert.Contains($"volume={expected}[l2]", arguments);
    }

    [Fact]
    public void BuildArguments_GivesEachSingersLeadItsOwnLevel()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null,
            new AudioMix(
            [
                new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
                new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"),
                new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal (♂)") { Voice = "♂" },
                new AudioTrack(3, AudioTrackRole.Lead, "Lead Vocal (♀)") { Voice = "♀" },
            ], LeadVolume: 10, BackingVolume: 100)
            {
                VoiceVolumes = new Dictionary<string, int> { ["♂"] = 60, ["♀"] = 20 },
            });

        Assert.Contains("[0:a:2]volume=0.600[l2]", arguments);
        Assert.Contains("[0:a:3]volume=0.200[l3]", arguments);
        // A pad label per track: two leads sharing one is a graph ffmpeg refuses to build.
        Assert.Contains("[m0][b1][l2][l3]amix=inputs=4:normalize=0", arguments);
    }

    [Fact]
    public void BuildArguments_ALeadWhoseVoiceHasNoLevel_RidesAtTheLeadLevel()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null,
            new AudioMix(
            [
                new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
                new AudioTrack(1, AudioTrackRole.Lead, "Lead Vocal (♂)") { Voice = "♂" },
                new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal"),
            ], LeadVolume: 30, BackingVolume: 100)
            {
                VoiceVolumes = new Dictionary<string, int> { ["♀"] = 90 },
            });

        Assert.Contains("[0:a:1]volume=0.300[l1]", arguments);
        Assert.Contains("[0:a:2]volume=0.300[l2]", arguments);
    }

    /// <summary>Named and ordered as the real files are: music first, then backing, then lead.</summary>
    private static AudioMix ThreeTrackMix(int lead, int backing) => new(
    [
        new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
        new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"),
        new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal"),
    ], lead, backing);

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }

    private static double AsetrateRatio(string arguments)
    {
        const string marker = "asetrate=44100*";
        var at = arguments.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return 1.0;

        at += marker.Length;
        var end = arguments.IndexOfAny([',', '"'], at);
        return double.Parse(arguments[at..end], CultureInfo.InvariantCulture);
    }

    private static List<double> AtempoFactors(string arguments)
    {
        var factors = new List<double>();
        const string marker = "atempo=";

        for (var at = arguments.IndexOf(marker, StringComparison.Ordinal); at >= 0;
             at = arguments.IndexOf(marker, at + 1, StringComparison.Ordinal))
        {
            var start = at + marker.Length;
            var end = arguments.IndexOfAny([',', '"'], start);
            factors.Add(double.Parse(arguments[start..end], CultureInfo.InvariantCulture));
        }

        return factors;
    }

    [Fact]
    public async Task OpenAsync_ThrowsForAMissingFile()
        => await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.OpenAsync(Path.Combine(_workingDirectory, "nope.mp4")));

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\secrets.txt")]
    [InlineData("sub/dir.ts")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void ResolveArtifact_RejectsAnythingThatIsNotABareFileName(string fileName)
        => Assert.Null(_service.ResolveArtifact("any-session", fileName));

    [Fact]
    public void ResolveArtifact_ReturnsNullForAnUnknownSession()
        => Assert.Null(_service.ResolveArtifact("no-such-session", "stream.m3u8"));

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
        GC.SuppressFinalize(this);
    }
}
