using System.Text.Json;

namespace KHost.IPC.SignalR;

/// <summary>
/// The wire form of every host↔screen message: the original serialized payload plus what
/// authenticates it. <see cref="ScreenId"/> names which key verifies it, <see cref="Seq"/> orders
/// it within the session, and <see cref="Mac"/> proves possession of that key over the session
/// nonce, the sequence and the payload.
/// </summary>
internal sealed record SignedEnvelope(string ScreenId, long Seq, string Payload, string Mac)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Null for anything that is not a well-formed envelope — a peer sending junk is refused, not thrown over.</summary>
    public static SignedEnvelope? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<SignedEnvelope>(json, Options); }
        catch (JsonException) { return null; }
    }
}
