using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KHost.Abstractions.Services.IPC;

namespace KHost.UnitTests.IPC;

// Guards the serialization contract that ScreenIpcSerializer (KHost.IPC.SignalR) depends on:
// polymorphic screen commands MUST be serialized through their base type so the $type
// discriminator is written. SignalR serializes invocation arguments by their concrete runtime
// type, which omits the discriminator and makes the receiver's base-typed deserialize throw —
// which is exactly why commands/state are sent across the wire as base-typed JSON strings.
public class ScreenCommandSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // Every concrete command must appear here. The RegisteredCommands_… tests fail if this
    // drifts from the [JsonDerivedType] list, so a new command cannot be added without
    // getting both an attribute and round-trip coverage.
    private static readonly Dictionary<string, ScreenCommandBase> Samples = new()
    {
        [nameof(LoadMediaCommand)] = new LoadMediaCommand { FilePath = "/music/x.mp4" },
        [nameof(PlayCommand)] = new PlayCommand(),
        [nameof(PauseCommand)] = new PauseCommand(),
        [nameof(StopCommand)] = new StopCommand { FadeDuration = TimeSpan.FromSeconds(2) },
        [nameof(SeekCommand)] = new SeekCommand { Position = TimeSpan.FromSeconds(42) },
        [nameof(SetVolumeCommand)] = new SetVolumeCommand { Volume = 0.75f },
        [nameof(SetPitchCommand)] = new SetPitchCommand { Semitones = -3 },
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
        IScreenCommand cmd = new LoadMediaCommand { FilePath = "/music/x.mp4" };

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
            (ScreenCommandBase)new LoadMediaCommand { FilePath = "/music/x.mp4" }, typeof(ScreenCommandBase), Options);

        var back = Assert.IsType<LoadMediaCommand>(JsonSerializer.Deserialize<ScreenCommandBase>(json, Options));
        Assert.Equal("/music/x.mp4", back.FilePath);
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
        Assert.Equal(-3, RoundTrip(new SetPitchCommand { Semitones = -3 }).Semitones);
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
            LoadedFilePath = "/music/x.mp4",
            IsPlaying = true,
            Position = TimeSpan.FromSeconds(5),
            Duration = TimeSpan.FromMinutes(3),
        };

        var json = JsonSerializer.Serialize(state, typeof(ScreenStateBase), Options);
        var back = JsonSerializer.Deserialize<ScreenStateBase>(json, Options);

        var playback = Assert.IsType<ScreenPlaybackState>(back);
        Assert.True(playback.IsPlaying);
        Assert.Equal("/music/x.mp4", playback.LoadedFilePath);
    }
}
