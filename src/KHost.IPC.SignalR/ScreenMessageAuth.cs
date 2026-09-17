using System.Security.Cryptography;
using System.Text;

namespace KHost.IPC.SignalR;

/// <summary>Signs and verifies screen messages with a per-screen key that never crosses the wire.</summary>
/// <remarks>The MAC covers the nonce and sequence too, so a capture can't be replayed or reordered.</remarks>
internal static class ScreenMessageAuth
{
    public static string Sign(byte[] key, string sessionNonce, long seq, string payload)
    {
        using var hmac = new HMACSHA256(key);
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(Canonical(sessionNonce, seq, payload)));
        return Convert.ToHexStringLower(mac);
    }

    public static bool Verify(byte[] key, string sessionNonce, long seq, string payload, string? mac)
    {
        var expected = Sign(key, sessionNonce, seq, payload);

        // Constant-time so a forger cannot tune bytes by timing the comparison. A wrong-length mac
        // fails immediately, which is fine: the MAC length is fixed and public.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(mac ?? string.Empty));
    }

    // Newline-separated so the three fields cannot run together, since otherwise ("a", 1, "b")
    // and ("a", 12, "") would sign the same bytes.
    private static string Canonical(string sessionNonce, long seq, string payload)
        => $"{sessionNonce}\n{seq}\n{payload}";
}
