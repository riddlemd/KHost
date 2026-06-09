using System.Text.Json;
using KHost.Abstractions.Services.IPC;

namespace KHost.IPC.SignalR;

/// <summary>
/// Commands and state are sent across SignalR as JSON strings serialized through their
/// polymorphic base types (<see cref="ScreenCommandBase"/> / <see cref="ScreenStateBase"/>).
/// This guarantees the <c>$type</c> discriminator is written: SignalR serializes invocation
/// arguments by their concrete runtime type, and System.Text.Json only emits the discriminator
/// when serializing through the polymorphic base — so passing the object directly would drop it
/// and the receiver's base-typed deserialize would fail.
/// </summary>
internal static class ScreenIpcSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string SerializeCommand(IScreenCommand command) =>
        JsonSerializer.Serialize(command, typeof(ScreenCommandBase), Options);

    public static IScreenCommand? DeserializeCommand(string json) =>
        JsonSerializer.Deserialize<ScreenCommandBase>(json, Options);

    public static string SerializeState(IScreenState state) =>
        JsonSerializer.Serialize(state, typeof(ScreenStateBase), Options);

    public static IScreenState? DeserializeState(string json) =>
        JsonSerializer.Deserialize<ScreenStateBase>(json, Options);
}
