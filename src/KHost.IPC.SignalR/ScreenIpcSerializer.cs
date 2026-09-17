using System.Text.Json;
using KHost.Abstractions.Services.IPC;

namespace KHost.IPC.SignalR;

/// <summary>Serializes through the polymorphic base types so <c>$type</c> is written.</summary>
/// <remarks>Serializing the concrete object directly drops <c>$type</c> and breaks deserialize.</remarks>
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
