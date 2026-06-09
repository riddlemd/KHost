using System.Text.Json;
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

    [Fact]
    public void SerializeByRuntimeType_OmitsDiscriminator()
    {
        IScreenCommand cmd = new LoadMediaCommand { FilePath = "/music/x.mp4" };

        // Mimics SignalR passing the argument as its concrete runtime type.
        var json = JsonSerializer.Serialize(cmd, cmd.GetType(), Options);

        Assert.DoesNotContain("$type", json);
    }

    [Fact]
    public void SerializeByBaseType_EmitsDiscriminator_AndRoundTrips()
    {
        ScreenCommandBase cmd = new LoadMediaCommand { FilePath = "/music/x.mp4" };

        var json = JsonSerializer.Serialize(cmd, typeof(ScreenCommandBase), Options);

        Assert.Contains("$type", json);

        var back = JsonSerializer.Deserialize<ScreenCommandBase>(json, Options);
        Assert.Equal("/music/x.mp4", Assert.IsType<LoadMediaCommand>(back).FilePath);
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
