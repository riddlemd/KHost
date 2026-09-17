using System.Text.Json;

namespace KHost.IPC.SignalR;

/// <summary>The wire form of every host↔screen message: the payload plus what authenticates it.</summary>
/// <remarks>ScreenId names the key, Seq orders it, Mac proves possession over nonce+seq+payload.</remarks>
internal sealed record SignedEnvelope(string ScreenId, long Seq, string Payload, string Mac)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Null for a malformed envelope: junk from a peer is refused, not thrown.</summary>
    public static SignedEnvelope? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<SignedEnvelope>(json, Options); }
        catch (JsonException) { return null; }
    }
}
