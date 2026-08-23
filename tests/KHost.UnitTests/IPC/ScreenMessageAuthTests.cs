using System.Text;
using KHost.IPC.SignalR;

namespace KHost.UnitTests.IPC;

public class ScreenMessageAuthTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("a-32-byte-key-for-the-screen-xx!");
    private const string Nonce = "session-nonce-abc";
    private const string Payload = "{\"position\":42}";

    [Fact]
    public void Sign_IsDeterministic()
        => Assert.Equal(
            ScreenMessageAuth.Sign(Key, Nonce, 1, Payload),
            ScreenMessageAuth.Sign(Key, Nonce, 1, Payload));

    [Fact]
    public void Verify_AcceptsAMessageSignedWithTheSameKeyNonceSeqAndPayload()
    {
        var mac = ScreenMessageAuth.Sign(Key, Nonce, 7, Payload);

        Assert.True(ScreenMessageAuth.Verify(Key, Nonce, 7, Payload, mac));
    }

    [Fact]
    public void Verify_RejectsATamperedPayload()
    {
        var mac = ScreenMessageAuth.Sign(Key, Nonce, 7, Payload);

        Assert.False(ScreenMessageAuth.Verify(Key, Nonce, 7, "{\"position\":999}", mac));
    }

    [Fact]
    public void Verify_RejectsADifferentSeq_SoAMessageCannotBeReplayedAtAnotherPosition()
    {
        var mac = ScreenMessageAuth.Sign(Key, Nonce, 7, Payload);

        Assert.False(ScreenMessageAuth.Verify(Key, Nonce, 8, Payload, mac));
    }

    [Fact]
    public void Verify_RejectsADifferentNonce_SoAMessageCannotBeReplayedIntoAnotherSession()
    {
        var mac = ScreenMessageAuth.Sign(Key, Nonce, 7, Payload);

        Assert.False(ScreenMessageAuth.Verify(Key, "a-different-session", 7, Payload, mac));
    }

    [Fact]
    public void Verify_RejectsAnotherScreensKey()
    {
        var mac = ScreenMessageAuth.Sign(Key, Nonce, 7, Payload);
        var otherKey = Encoding.UTF8.GetBytes("a-different-32-byte-screen-key!!!");

        Assert.False(ScreenMessageAuth.Verify(otherKey, Nonce, 7, Payload, mac));
    }

    [Fact]
    public void Verify_KeepsTheSeqAndPayloadBoundaryDistinct()
    {
        // Without a field separator these two collide: "s" + "1" + "23x" == "s" + "12" + "3x".
        // A MAC for one must never verify the other, or a seq could be shifted into the payload.
        var mac = ScreenMessageAuth.Sign(Key, "s", 1, "23x");

        Assert.False(ScreenMessageAuth.Verify(Key, "s", 12, "3x", mac));
    }

    [Fact]
    public void Verify_RejectsAMissingOrMalformedMac()
    {
        Assert.False(ScreenMessageAuth.Verify(Key, Nonce, 7, Payload, null));
        Assert.False(ScreenMessageAuth.Verify(Key, Nonce, 7, Payload, ""));
        Assert.False(ScreenMessageAuth.Verify(Key, Nonce, 7, Payload, "not-a-real-mac"));
    }
}
