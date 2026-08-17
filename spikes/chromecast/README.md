# chromecast sender spike

Throwaway. Answers: can an off-the-shelf .NET sender drive a Cast receiver, and specifically
can it drive the emulator at `~/Developer/riddlemd/Chromecast-Emulator`, whose TLS certificate
is self-signed and cannot chain to Google's Eureka CA?

Outside `KHost.slnx` and opted out of central package management.

```bash
# terminal 1 — the fake receiver
cd ~/Developer/riddlemd/Chromecast-Emulator/src/ChromecastEmulator
dotnet run -- --name "KHost Test Cast" --no-console

# terminal 2
cd spikes/chromecast/KHost.Spike.CastSender
dotnet run -- --host 127.0.0.1     # or --api to dump Sharpcaster's surface
```

## Findings

**Sharpcaster 3.0.0 works against the emulator.** The self-signed certificate is not a problem:
like every Cast library it cannot verify the Eureka chain either, so it does not try. Discovery,
connect, `LAUNCH CC1AD845`, `LOAD`, play/pause/seek and `MediaChannel.StatusChanged` position
reports all behaved.

**`MediaChannel.SetVolumeAsync` is unusable** — it throws `MediaSessionID is not available` even
with a live session, which looks like Sharpcaster's own state tracking rather than the protocol.
Stream volume would have been the polite way to mute, because it leaves the TV's own level alone.

**`ReceiverChannel.SetMute(true)` works, and is therefore the mute path.** The cost is that it is
device-global: it mutes the receiver, not just our stream, so it moves the TV's volume state. On a
receiver reporting `control_type: "fixed"` it would be refused outright — a real device can decline
to be muted, and the caller has to tolerate that.

**Discovery returns a `DeviceUri` with no explicit port** (`https://<ip>/`); Sharpcaster fills in
8009 itself. It also reported an address on a different interface than the one the host serves
media from, which matters: the media URL handed to a Cast device has to be LAN-routable from the
*device*, not from the host.
