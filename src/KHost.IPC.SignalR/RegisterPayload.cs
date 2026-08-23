using System.Text.Json;
using KHost.Abstractions.Services.IPC;

namespace KHost.IPC.SignalR;

/// <summary>
/// The signed body of a registration: the capabilities a screen declares. The screen id is not here
/// — it names the key on the envelope, so it is authenticated as part of the MAC either way.
/// </summary>
internal sealed record RegisterPayload(bool SupportsSync, bool SupportsAudio, bool SupportsVideo)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static RegisterPayload From(ScreenCapabilities capabilities)
        => new(capabilities.SupportsSync, capabilities.SupportsAudio, capabilities.SupportsVideo);

    public ScreenCapabilities ToCapabilities()
        => new() { SupportsSync = SupportsSync, SupportsAudio = SupportsAudio, SupportsVideo = SupportsVideo };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static RegisterPayload? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<RegisterPayload>(json, Options); }
        catch (JsonException) { return null; }
    }
}
