using System.Buffers.Binary;
using KHost.Abstractions.Models;
using KHost.Domain.Services.Displays.LocalScreen;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

public class SongLevelsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-levels-{Guid.NewGuid():n}");

    // Bands: 1 is 50-125Hz, 6 is 2800-5600Hz.
    private const int LowBand = 1;
    private const int HighBand = 6;

    private static DisplayLoad StemsLoad(params StemSource[] stems) => new() { Stems = stems };

    // --- which audio ---

    [Fact]
    public void InputsFor_Stems_ReadsEachAtItsLoadedLevel_AndLeavesOutAMutedOne()
    {
        var load = StemsLoad(
            new StemSource(0, AudioTrackRole.Music, "http://host/media/s/music.ogg", 100),
            new StemSource(1, AudioTrackRole.Lead, "http://host/media/s/lead.ogg", 0),
            new StemSource(2, AudioTrackRole.Backing, "http://host/media/s/backing.ogg", 60));

        var inputs = SongLevels.InputsFor("/songs/africa.song", load, url => "/disk/" + url[(url.LastIndexOf('/') + 1)..]);

        Assert.Equal([new SongLevelsInput("/disk/music.ogg", 100), new SongLevelsInput("/disk/backing.ogg", 60)], inputs);
    }

    [Fact]
    public void InputsFor_AStemThatCannotBeOpened_ReadsNothing()
    {
        var load = StemsLoad(
            new StemSource(0, AudioTrackRole.Music, "http://host/media/s/music.ogg", 100),
            new StemSource(1, AudioTrackRole.Backing, "http://host/media/s/gone.ogg", 60));

        // Half a mix would draw a beat the room is not hearing.
        Assert.Null(SongLevels.InputsFor("/songs/africa.song", load, url => url.Contains("gone") ? null : url));
    }

    [Fact]
    public void InputsFor_EveryStemMuted_ReadsNothing()
    {
        var load = StemsLoad(new StemSource(0, AudioTrackRole.Lead, "http://host/media/s/lead.ogg", 0));

        Assert.Null(SongLevels.InputsFor("/songs/africa.song", load, url => url));
    }

    [Theory]
    [InlineData("/songs/africa.mp4")]
    [InlineData("/songs/africa.MP3")]
    public void InputsFor_AFileTheHostOpens_ReadsTheFile(string path)
    {
        Assert.Equal([new SongLevelsInput(path)], SongLevels.InputsFor(path, new DisplayLoad { StreamUrl = "http://s" }, _ => null));
    }

    [Fact]
    public void InputsFor_AProvidersOwnContainerWithNoStems_ReadsNothing()
    {
        Assert.Null(SongLevels.InputsFor("/songs/africa.song", new DisplayLoad { StreamUrl = "http://s" }, url => url));
    }

    [Fact]
    public void InputsFor_ACdg_ReadsTheAudioBesideIt()
    {
        Directory.CreateDirectory(_directory);
        var graphics = Path.Combine(_directory, "Africa.cdg");
        var audio = Path.Combine(_directory, "africa.mp3");
        File.WriteAllText(graphics, "");
        File.WriteAllText(audio, "");

        Assert.Equal([new SongLevelsInput(audio)], SongLevels.InputsFor(graphics, new DisplayLoad { StreamUrl = "http://s" }, _ => null));
    }

    [Fact]
    public void InputsFor_ACdgWithNoAudio_ReadsNothing()
    {
        Directory.CreateDirectory(_directory);
        var graphics = Path.Combine(_directory, "Africa.cdg");
        File.WriteAllText(graphics, "");

        Assert.Null(SongLevels.InputsFor(graphics, new DisplayLoad { StreamUrl = "http://s" }, _ => null));
    }

    // --- ffmpeg's arguments ---

    [Fact]
    public void BuildArguments_OneFile_DecodesItsFirstAudioToStereoPcm()
    {
        var arguments = SongLevels.BuildArguments([new SongLevelsInput("/songs/a \"b\".mp4")]);

        Assert.Equal(
            ["-hide_banner", "-nostdin", "-loglevel", "error", "-threads", "1",
             "-vn", "-i", "/songs/a \"b\".mp4", "-map", "0:a:0",
             "-ac", "2", "-ar", "22050", "-f", "s16le", "pipe:1"],
            arguments);
    }

    [Fact]
    public void BuildArguments_Stems_SumsThemAtTheirLevels_WithoutAmixDividing()
    {
        var arguments = SongLevels.BuildArguments([new SongLevelsInput("/m.ogg", 100), new SongLevelsInput("/b.ogg", 50)]);

        var graph = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Equal("[0:a:0]volume=1.000[s0];[1:a:0]volume=0.500[s1];[s0][s1]amix=inputs=2:normalize=0[x]", graph);
        Assert.Equal(["-i", "/m.ogg"], arguments.Skip(7).Take(2));
        Assert.Equal(["-i", "/b.ogg"], arguments.Skip(10).Take(2));
        Assert.Contains("[x]", arguments);
    }

    [Fact]
    public void BuildArguments_OneStemBelowFull_StillTakesItsLevel()
    {
        var arguments = SongLevels.BuildArguments([new SongLevelsInput("/b.ogg", 40)]);

        Assert.Contains("[0:a:0]volume=0.400[s0];[s0]amix=inputs=1:normalize=0[x]", arguments);
    }

    [Fact]
    public void BuildArguments_Nothing_Throws()
    {
        Assert.Throws<ArgumentException>(() => SongLevels.BuildArguments([]));
    }

    // --- reading PCM ---

    [Fact]
    public async Task AnalyseAsync_ToneThenSilence_ReadsLoudThenNothing_ThirtyFramesASecond()
    {
        // One second of 100Hz on the left and 4kHz on the right, then one of silence.
        var samples = SongLevels.SampleRate * 2;
        var pcm = new byte[samples * 4];
        for (var n = 0; n < SongLevels.SampleRate; n++)
        {
            var t = (double)n / SongLevels.SampleRate;
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(n * 4), (short)(16000 * Math.Sin(2 * Math.PI * 100 * t)));
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(n * 4 + 2), (short)(16000 * Math.Sin(2 * Math.PI * 4000 * t)));
        }

        // Dribbled a few odd bytes at a time: a read that ends mid-sample must not shift the rest.
        var frames = SongLevels.Decode(await SongLevels.AnalyseAsync(new DribbleStream(pcm, 7)))!;

        Assert.Equal(60, frames.Length);
        Assert.All(frames[2..28], frame =>
        {
            Assert.True(frame[0][LowBand] > 240, "left carries the bass");
            Assert.True(frame[0][HighBand] < 60, "left carries no treble");
            Assert.True(frame[1][HighBand] > 240, "right carries the treble");
            Assert.True(frame[1][LowBand] < 60, "right carries no bass");
        });
        Assert.All(frames[32..], frame => Assert.All(frame, channel => Assert.All(channel, level => Assert.Equal(0, level))));
    }

    [Fact]
    public async Task AnalyseAsync_ShortOfOneWindow_StillReadsAFrame()
    {
        var frames = SongLevels.Decode(await SongLevels.AnalyseAsync(new MemoryStream(new byte[100 * 4])))!;

        Assert.Single(frames);
    }

    // --- the track ---

    [Fact]
    public void Encode_ThenDecode_KeepsEveryLevel_EachBandAgainstItsOwnLoudest()
    {
        var bands = SongLevels.Bands;
        double[][] Frame(double left, double right) => [Enumerable.Repeat(left, bands).ToArray(), Enumerable.Repeat(right, bands).ToArray()];

        var track = SongLevels.Encode([Frame(-10, -20), Frame(0, double.NegativeInfinity)]);

        Assert.Equal("KHLV"u8.ToArray(), track[..4]);
        Assert.Equal([SongLevels.Version, SongLevels.FramesPerSecond, (byte)bands, SongLevels.Channels], track[4..8]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(track.AsSpan(8)));
        Assert.Equal(SongLevels.HeaderLength + 2 * 2 * bands, track.Length);

        var frames = SongLevels.Decode(track)!;
        Assert.All(frames[0][0], level => Assert.Equal(215, level));   // 10dB under: 255 - 40
        Assert.All(frames[0][1], level => Assert.Equal(175, level));   // 20dB under
        Assert.All(frames[1][0], level => Assert.Equal(255, level));
        Assert.All(frames[1][1], level => Assert.Equal(0, level));
    }

    [Fact]
    public void Encode_ABandThatStaysFarUnderTheRest_IsNotStretchedToFull()
    {
        var bands = SongLevels.Bands;
        var loud = Enumerable.Repeat(0.0, bands).ToArray();
        loud[0] = -60;   // hiss, never within 40dB of the song

        var frames = SongLevels.Decode(SongLevels.Encode([[loud, loud]]))!;

        Assert.Equal(175, frames[0][0][0]);   // 20dB under the 40dB floor it is measured against
        Assert.Equal(255, frames[0][0][1]);
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(5, 255)]
    [InlineData(-63.75, 0)]
    [InlineData(-90, 0)]
    [InlineData(-0.25, 254)]
    public void Quantise_QuarterDecibelSteps_ClampedToAByte(double decibels, int expected)
    {
        Assert.Equal(expected, SongLevels.Quantise(decibels, 0));
    }

    [Fact]
    public void Decode_NotATrack_AnswersNull()
    {
        var track = SongLevels.Encode([[new double[SongLevels.Bands], new double[SongLevels.Bands]]]);

        var badMagic = (byte[])track.Clone(); badMagic[0] = (byte)'X';
        var badVersion = (byte[])track.Clone(); badVersion[4] = 9;

        Assert.Null(SongLevels.Decode(badMagic));
        Assert.Null(SongLevels.Decode(badVersion));
        Assert.Null(SongLevels.Decode(track[..^1]));
        Assert.Null(SongLevels.Decode([.. track, 0]));
        Assert.Null(SongLevels.Decode(track[..5]));
        Assert.NotNull(SongLevels.Decode(track));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* never made, or swept by the OS */ }

        GC.SuppressFinalize(this);
    }

    /// <summary>Hands out at most a few bytes a read, as a pipe may.</summary>
    private sealed class DribbleStream(byte[] data, int chunk) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], cancellationToken);
    }
}
