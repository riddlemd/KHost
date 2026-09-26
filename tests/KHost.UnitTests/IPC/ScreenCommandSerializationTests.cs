using KHost.Abstractions.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KHost.IPC.SignalR.Contracts;

namespace KHost.UnitTests.IPC;

// Guards ScreenIpcSerializer's contract: commands must serialize through their base type or the
// $type discriminator is dropped and the receiver's base-typed deserialize throws.
public class ScreenCommandSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // Every concrete command must appear here. The RegisteredCommands_… tests fail if this
    // drifts from the [JsonDerivedType] list.
    private static readonly Dictionary<string, ScreenCommandBase> Samples = new()
    {
        [nameof(LoadMediaCommand)] = new LoadMediaCommand { StreamUrl = "/music/x.mp4" },
        [nameof(PlayCommand)] = new PlayCommand(),
        [nameof(PauseCommand)] = new PauseCommand(),
        [nameof(StopCommand)] = new StopCommand { FadeDuration = TimeSpan.FromSeconds(2) },
        [nameof(SeekCommand)] = new SeekCommand { Position = TimeSpan.FromSeconds(42) },
        [nameof(SetVolumeCommand)] = new SetVolumeCommand { Volume = 0.75f },
        [nameof(SetStemVolumeCommand)] = new SetStemVolumeCommand
        {
            Role = AudioTrackRole.Lead,
            Volume = 40,
        },
        [nameof(SetVideoCommand)] = new SetVideoCommand { Enabled = false },
        [nameof(LoadBackgroundCommand)] = new LoadBackgroundCommand { StreamUrl = "/music/bed.m3u8", AutoPlay = true },
        [nameof(PlayBackgroundCommand)] = new PlayBackgroundCommand(),
        [nameof(PauseBackgroundCommand)] = new PauseBackgroundCommand(),
        [nameof(StopBackgroundCommand)] = new StopBackgroundCommand { FadeDuration = TimeSpan.FromSeconds(2) },
        [nameof(SetBackgroundVolumeCommand)] = new SetBackgroundVolumeCommand { Volume = 0.4f },
        [nameof(ShowImageCommand)] = new ShowImageCommand { Url = "http://host/media/image/abc", Scaling = ImageScaling.Fill },
        [nameof(HideImageCommand)] = new HideImageCommand(),
        [nameof(SetMarqueeCommand)] = new SetMarqueeCommand
        {
            Enabled = true,
            Singers = ["Ada", "Grace"],
            Message = "Happy hour until 8",
            Position = MarqueePosition.Top,
            BackgroundColor = "#101820",
            TextColor = "#f2f2f5",
            FontSizePixels = 36,
            ScrollSpeed = 140,
            PinLabel = true,
        },
        [nameof(SetBreakMusicCardCommand)] = new SetBreakMusicCardCommand
        {
            Enabled = true,
            Title = "Free Fallin'",
            Artist = "Tom Petty",
            Corner = OverlayCorner.BottomLeft,
            Offset = 1.5,
        },
        [nameof(SetTimedLyricsCommand)] = new SetTimedLyricsCommand
        {
            Lyrics = new TimedLyrics
            {
                DurationSeconds = 362.1,
                Bounds = new LyricBox(0, 0, 640, 360),
                Pages =
                [
                    new LyricPage
                    {
                        ShowFromSeconds = 1.5,
                        ShowUntilSeconds = 6.25,
                        Active = new LyricColor(255, 240, 0),
                        Inactive = new LyricColor(255, 255, 255),
                        Lines =
                        [
                            new LyricLine
                            {
                                Position = new LyricBox(40, 120, 560, 48),
                                Syllables =
                                [
                                    new LyricSyllable(1.5, 1.9, "Turn"),
                                    new LyricSyllable(1.9, 2.4, " a"),
                                    new LyricSyllable(2.4, 3.0, "round"),
                                ],
                            },
                        ],
                    },
                ],
            },
        },
        [nameof(ShowNextSingerCommand)] = new ShowNextSingerCommand
        {
            Singer = "Ada",
            Song = "Total Eclipse of the Heart",
            Artist = "Bonnie Tyler",
        },
        [nameof(SetScreenQrCodesCommand)] = new SetScreenQrCodesCommand
        {
            Codes =
            [
                new ScreenQrCodePlacement
                {
                    ImageUrl = "data:image/svg+xml;base64,PHN2Zy8+",
                    Caption = "Scan to join the queue",
                    Corner = OverlayCorner.TopLeft,
                    Size = QrCodeSize.Large,
                },
            ],
        },
    };

    public static TheoryData<string> CommandNames => [.. Samples.Keys];

    private static IReadOnlyList<Type> ConcreteSubtypesOf<TBase>() =>
        [.. typeof(TBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(TBase).IsAssignableFrom(t))];

    private static IReadOnlyList<Type> RegisteredSubtypesOf<TBase>() =>
        [.. typeof(TBase).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.DerivedType)];

    [Fact]
    public void SerializeByRuntimeType_OmitsDiscriminator()
    {
        IScreenCommand cmd = new LoadMediaCommand { StreamUrl = "/music/x.mp4" };

        // Mimics SignalR passing the argument as its concrete runtime type.
        var json = JsonSerializer.Serialize(cmd, cmd.GetType(), Options);

        Assert.DoesNotContain("$type", json);
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void EveryCommand_SerializedByBaseType_EmitsDiscriminatorAndRoundTrips(string commandName)
    {
        var original = Samples[commandName];

        var json = JsonSerializer.Serialize(original, typeof(ScreenCommandBase), Options);

        Assert.Contains("$type", json);

        var back = JsonSerializer.Deserialize<ScreenCommandBase>(json, Options);

        Assert.NotNull(back);
        Assert.IsType(original.GetType(), back);
    }

    [Fact]
    public void RoundTrip_PreservesLoadMediaPayload()
    {
        var json = JsonSerializer.Serialize(
            (ScreenCommandBase)new LoadMediaCommand { StreamUrl = "http://host/media/a/stream.m3u8" },
            typeof(ScreenCommandBase), Options);

        var back = Assert.IsType<LoadMediaCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(json, Options));
        Assert.Equal("http://host/media/a/stream.m3u8", back.StreamUrl);
    }

    [Fact]
    public void RoundTrip_KeepsTheSingerAStemAndALevelBelongTo()
    {
        var load = new LoadMediaCommand
        {
            Stems = [new StemSource(2, AudioTrackRole.Lead, "http://host/l.ogg", 0) { Voice = "♀" }],
        };
        var level = new SetStemVolumeCommand { Role = AudioTrackRole.Lead, Voice = "♀", Volume = 55 };

        var loaded = Assert.IsType<LoadMediaCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(
            JsonSerializer.Serialize(load, typeof(ScreenCommandBase), Options), Options));
        var moved = Assert.IsType<SetStemVolumeCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(
            JsonSerializer.Serialize(level, typeof(ScreenCommandBase), Options), Options));

        // Lost on the wire, a named singer's stem would answer to the unnamed lead's fader.
        Assert.Equal("♀", Assert.Single(loaded.Stems).Voice);
        Assert.Equal("♀", moved.Voice);
    }

    /// <summary>The timing travels as the contract type itself, so everything the screen draws
    /// between the words has to survive the wire as well as the words do.</summary>
    [Fact]
    public void RoundTrip_KeepsTheCountInsAndLeadIns()
    {
        var countIn = new LyricCountIn
        {
            StartSeconds = 165.38,
            EndSeconds = 186.4,
            Position = new LyricBox(60, 282.5, 520, 35),
            StepSeconds = 1,
            Steps = 4,
            Active = new LyricColor(245, 44, 119),
            Inactive = new LyricColor(253, 215, 230),
            Border = new LyricColor(0, 0, 0),
            BorderWidth = 2.5,
        };
        var command = new SetTimedLyricsCommand
        {
            Lyrics = new TimedLyrics
            {
                DurationSeconds = 300,
                Bounds = new LyricBox(0, 0, 640, 377.5),
                CountIns = [countIn],
                Pages = [new LyricPage { ShowFromSeconds = 185.2, ShowUntilSeconds = 189.4, Lines = [new LyricLine { LeadIn = new LyricLeadIn(185.2, 110.5) }] }],
            },
        };

        var back = Assert.IsType<SetTimedLyricsCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(
            JsonSerializer.Serialize(command, typeof(ScreenCommandBase), Options), Options));

        Assert.Equal(countIn, Assert.Single(back.Lyrics!.CountIns));
        Assert.Equal(new LyricLeadIn(185.2, 110.5), back.Lyrics.Pages[0].Lines[0].LeadIn);
    }

    [Fact]
    public void RoundTrip_KeepsTheIntroCard()
    {
        var command = new SetTimedLyricsCommand
        {
            Lyrics = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) },
            Intro = new ScreenIntroCard { Title = "Africa", Artist = "Toto", Singer = "DJ P" },
        };

        var back = Assert.IsType<SetTimedLyricsCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(
            JsonSerializer.Serialize(command, typeof(ScreenCommandBase), Options), Options));

        Assert.NotNull(back.Intro);
        Assert.Equal(("Africa", "Toto", "DJ P"), (back.Intro.Title, back.Intro.Artist, back.Intro.Singer));
    }

    /// <summary>Dropped on the wire, a song whose words start at once would start at once.</summary>
    [Fact]
    public void RoundTrip_KeepsTheLeadIn()
    {
        var command = new SetTimedLyricsCommand
        {
            Lyrics = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) },
            LeadInSeconds = 3.8,
        };

        var back = Assert.IsType<SetTimedLyricsCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(
            JsonSerializer.Serialize(command, typeof(ScreenCommandBase), Options), Options));

        Assert.Equal(3.8, back.LeadInSeconds);
    }

    /// <summary>A song without a card must arrive without one, not with an empty card to draw.</summary>
    [Fact]
    public void RoundTrip_NoIntroCard_StaysNone()
    {
        var command = new SetTimedLyricsCommand { Lyrics = null };

        var back = Assert.IsType<SetTimedLyricsCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(
            JsonSerializer.Serialize(command, typeof(ScreenCommandBase), Options), Options));

        Assert.Null(back.Intro);
    }

    [Fact]
    public void RoundTrip_PreservesCommandPayloads()
    {
        static T RoundTrip<T>(T command) where T : ScreenCommandBase
        {
            var json = JsonSerializer.Serialize(command, typeof(ScreenCommandBase), Options);
            return Assert.IsType<T>(JsonSerializer.Deserialize<ScreenCommandBase>(json, Options));
        }

        Assert.Equal(TimeSpan.FromSeconds(42), RoundTrip(new SeekCommand { Position = TimeSpan.FromSeconds(42) }).Position);
        Assert.Equal(0.75f, RoundTrip(new SetVolumeCommand { Volume = 0.75f }).Volume);
        Assert.Equal(TimeSpan.FromSeconds(2), RoundTrip(new StopCommand { FadeDuration = TimeSpan.FromSeconds(2) }).FadeDuration);
        Assert.Null(RoundTrip(new StopCommand()).FadeDuration);

        // The role names the voice across the wire, so it has to survive as itself rather than as
        // whatever number the enum happens to sit at.
        var stem = RoundTrip(new SetStemVolumeCommand { Role = AudioTrackRole.Backing, Volume = 65 });
        Assert.Equal(AudioTrackRole.Backing, stem.Role);
        Assert.Equal(65, stem.Volume);
    }

    [Fact]
    public void RegisteredCommands_CoverEveryConcreteCommandType()
    {
        var missing = ConcreteSubtypesOf<ScreenCommandBase>()
            .Except(RegisteredSubtypesOf<ScreenCommandBase>())
            .Select(t => t.Name)
            .OrderBy(n => n);

        Assert.True(!missing.Any(),
            $"Missing [JsonDerivedType] on ScreenCommandBase for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void RegisteredStates_CoverEveryConcreteStateType()
    {
        var missing = ConcreteSubtypesOf<ScreenStateBase>()
            .Except(RegisteredSubtypesOf<ScreenStateBase>())
            .Select(t => t.Name)
            .OrderBy(n => n);

        Assert.True(!missing.Any(),
            $"Missing [JsonDerivedType] on ScreenStateBase for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void SampleCommands_CoverEveryRegisteredCommandType()
    {
        var covered = Samples.Values.Select(c => c.GetType());

        var uncovered = RegisteredSubtypesOf<ScreenCommandBase>()
            .Except(covered)
            .Select(t => t.Name)
            .OrderBy(n => n);

        Assert.True(!uncovered.Any(),
            $"No round-trip sample for: {string.Join(", ", uncovered)}");
    }

    [Fact]
    public void CommandDiscriminators_AreUnique()
    {
        var duplicates = typeof(ScreenCommandBase)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .GroupBy(a => a.TypeDiscriminator?.ToString())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.True(!duplicates.Any(),
            $"Duplicate $type discriminators: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void State_SerializeByBaseType_RoundTrips()
    {
        ScreenStateBase state = new ScreenPlaybackState
        {
            StreamUrl = "http://192.168.1.10:5251/media/abc123/stream.m3u8",
            IsPlaying = true,
            Position = TimeSpan.FromSeconds(5),
            Duration = TimeSpan.FromMinutes(3),
        };

        var json = JsonSerializer.Serialize(state, typeof(ScreenStateBase), Options);
        var back = JsonSerializer.Deserialize<ScreenStateBase>(json, Options);

        var playback = Assert.IsType<ScreenPlaybackState>(back);
        Assert.True(playback.IsPlaying);
        Assert.Equal("http://192.168.1.10:5251/media/abc123/stream.m3u8", playback.StreamUrl);
    }

    // The two states share a base, so a background report that deserialized as playback would be
    // read by PlaybackService as the song's own position.
    [Fact]
    public void BackgroundState_SerializeByBaseType_RoundTripsAsItsOwnType()
    {
        ScreenStateBase state = new ScreenBackgroundState
        {
            StreamUrl = "http://192.168.1.10:5251/media/bed99/stream.m3u8",
            IsPlaying = false,
            HasEnded = true,
        };

        var json = JsonSerializer.Serialize(state, typeof(ScreenStateBase), Options);
        var back = JsonSerializer.Deserialize<ScreenStateBase>(json, Options);

        var background = Assert.IsType<ScreenBackgroundState>(back);
        Assert.True(background.HasEnded);
        Assert.False(background.IsPlaying);
        Assert.Equal("http://192.168.1.10:5251/media/bed99/stream.m3u8", background.StreamUrl);
    }
}
