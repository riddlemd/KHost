using System.Text.Json;
using KHost.IPC.SignalR.Contracts;

namespace KHost.IPC.SignalR;

/// <summary>The signed body of a registration.</summary>
/// <remarks>No screen id: it names the envelope's key, so the MAC covers it either way.</remarks>
internal sealed record RegisterPayload(bool SupportsAudio, bool SupportsVideo)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static RegisterPayload From(ScreenCapabilities capabilities)
        => new(capabilities.SupportsAudio, capabilities.SupportsVideo);

    public ScreenCapabilities ToCapabilities()
        => new() { SupportsAudio = SupportsAudio, SupportsVideo = SupportsVideo };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static RegisterPayload? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<RegisterPayload>(json, Options); }
        catch (JsonException) { return null; }
    }
}
