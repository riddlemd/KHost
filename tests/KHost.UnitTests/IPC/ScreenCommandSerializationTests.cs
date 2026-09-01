using KHost.Abstractions.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KHost.Abstractions.Services.IPC;

namespace KHost.UnitTests.IPC;

// Guards ScreenIpcSerializer's contract: commands must serialize through their base type or the
// $type discriminator is dropped and the receiver's base-typed deserialize throws.
public class ScreenCommandSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // Every concrete command must appear here — the RegisteredCommands_… tests fail if this
    // drifts from the [JsonDerivedType] list.
    private static readonly Dictionary<string, ScreenCommandBase> Samples = new()
    {
        [nameof(LoadMediaCommand)] = new LoadMediaCommand { StreamUrl = "/music/x.mp4" },
        [nameof(PlayCommand)] = new PlayCommand(),
        [nameof(PauseCommand)] = new PauseCommand(),
        [nameof(StopCommand)] = new StopCommand { FadeDuration = TimeSpan.FromSeconds(2) },
        [nameof(SeekCommand)] = new SeekCommand { Position = TimeSpan.FromSeconds(42) },
        [nameof(SetVolumeCommand)] = new SetVolumeCommand { Volume = 0.75f },
        [nameof(SetVideoCommand)] = new SetVideoCommand { Enabled = false },
        [nameof(SetTimelineCommand)] = new SetTimelineCommand
        {
            Position = TimeSpan.FromSeconds(42),
            AnchorUtc = new DateTime(2026, 8, 17, 20, 30, 0, DateTimeKind.Utc),
            IsPlaying = true,
            IsPrimary = true,
        },
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
