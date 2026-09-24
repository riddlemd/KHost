# What an `IDisplayProvider` is

**Implemented.** `IDisplayProvider` is in `KHost.Abstractions/Services/`, the screens reach it
through `KHost.Domain/Services/Displays/LocalScreen/LocalScreenDisplayProvider.cs`, and `PlaybackService` drives
whichever one is connected through one dispatcher. The interface itself is the authority on the current
surface; this note keeps the reasoning behind its shape.

> A display provider owns **a transport to places the song comes out**, and everything the host can
> put on them. It finds such places, connects to one, hands it a stream, drives transport on it, and
> draws on it. It does not decide *what* the show is — it is told.

## Why it exists

`IDisplayProvider` arrived narrowly, to get Chromecast out of the host. A screen was driven through
something else entirely: `IScreenServer`, twenty commands, its own registration handshake, roles and
sync. Two kinds of output with no shared vocabulary.

That cost something real. When kit rendering moved to the screen, the words went out as
`SetTimedLyricsCommand` — a *screen* command — so a connected Chromecast silently played a stream
with no words on it, and a `.kit` with no picture at all. Nothing in the design caught it, because
nothing in the design said these were two of the same thing.

A screen is not a plugin. But the screens should reach the host **through a provider**, so there is
one answer to "where does the song come out, and what can it show?"

## The two implementations

| | `LocalScreenDisplayProvider` (core) | `ChromecastDisplayProvider` (plugin) |
|---|---|---|
| Transport | SignalR IPC over loopback | CASTV2 across the LAN |
| Reach | same machine only | off-box |
| Finding devices | launching one — the host starts the process | mDNS browse |
| Drawable surface | all of it | none; inherits the defaults |

The screens provider is **core logic**, registered by the host. It travels the same path a plugin's
display travels, but `PluginLoader` must not bind it and it must never appear on the Plugins page.

Local-only screens sharpen `discovery` into two different acts wearing one name. For Cast it is a
real network sweep that must be started and stopped. For screens there is nothing to find: the host
launches the process and it registers back. Say so in the member's own docs rather than pretending
they are the same.

## The shape

```csharp
public interface IDisplayProvider
{
    // --- identity ---

    /// <summary>Names the transport for the console, so no wording is built into the host.</summary>
    string Name { get; }

    // --- discovery ---

    bool IsDiscovering { get; }
    Task StartDiscoveryAsync(CancellationToken cancellationToken = default);
    Task StopDiscoveryAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<DisplayDevice> Devices { get; }

    // --- connection ---

    /// <summary>The one device this transport drives; every argument-free member addresses it.</summary>
    string? ConnectedDeviceId { get; }
    Guid? SessionId { get; }

    /// <summary>Refused, not a replacement, while a different device is connected.</summary>
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged;

    // --- transport ---

    Task LoadAsync(string streamUrl, TimeSpan startOffset, int tempo = 0, CancellationToken ct = default);
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
    Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default);

    // --- drawable: every one has a default body ---
    //
    // This is what makes the full surface honest for a device that cannot draw. A Cast plugin
    // implements the six transport members and ignores the rest; it does not write ten stubs.
    // Precedent: IMediaPlaybackGate.Claims, IPluginButtonHandler.DescribeButton.

    Task SetTimedLyricsAsync(TimedLyrics? lyrics, CancellationToken ct = default) => Task.CompletedTask;
    Task SetMarqueeAsync(MarqueeState? marquee, CancellationToken ct = default) => Task.CompletedTask;
    Task SetQrCodesAsync(IReadOnlyList<ScreenQrCodePlacement> codes, CancellationToken ct = default) => Task.CompletedTask;
    Task ShowNextSingerAsync(NextSingerCard card, CancellationToken ct = default) => Task.CompletedTask;
    Task SetBreakMusicCardAsync(BreakMusicCard? card, CancellationToken ct = default) => Task.CompletedTask;
    Task ShowImageAsync(string imageUrl, CancellationToken ct = default) => Task.CompletedTask;
    Task HideImageAsync(CancellationToken ct = default) => Task.CompletedTask;
    Task LoadBackgroundAsync(string url, CancellationToken ct = default) => Task.CompletedTask;
    Task SetBackgroundVolumeAsync(double volume, CancellationToken ct = default) => Task.CompletedTask;
    Task SetVideoAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
}
```

There is deliberately **no timeline or sync member, and no device count**. One display, full stop:
the local screen or a receiver, never both and never two of either. A plugin display handles its
own communication with its device, so the host keeps no seam for several — the display that is up
defines the song's clock and nothing is steered onto anything else.

## What a device carries

`ScreenCapabilities` folds into `DisplayDevice`, because it was always describing a device rather
than a transport.

```csharp
public sealed class DisplayDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }
    public bool IsConnected { get; init; }

    public bool SupportsAudio { get; init; }
    public bool SupportsVideo { get; init; }

    /// <summary>Draws a song's words itself, from the timing the host hands over.</summary>
    public bool SupportsLyrics { get; init; }

    public bool SupportsMarquee { get; init; }
    public bool SupportsQrCodes { get; init; }

    /// <summary>Rides the song down on a stop rather than cutting it dead.</summary>
    public bool SupportsFade { get; init; }

    /// <summary>Anything put up as a picture rather than a layer: the next-singer card, the
    /// break-music card, and a plain shown image, which already share that shape.</summary>
    public bool SupportsImage { get; init; }
}
```

They sit on the device, not the provider: a provider may reach devices of differing ability, and the
host is asking about the thing in the room. LocalScreen answers true to all of them; a Cast receiver
answers false to the four drawable ones.

### One flag per overlay, not one flag for all

Because the host's answer to each differs, and collapsing them would hide that.

| Overlay | Fixed for the song? | If the device cannot draw it |
|---|---|---|
| Lyrics | **yes** — every syllable known at load | **burn into the stream** |
| Marquee | no — scrolls, text changes on a venue edit | leave it off |
| QR codes | no — placement can change mid-song | leave it off |
| Images / cards | no — appear on a host's action | leave it off |

`SupportsFade` is the odd one out: it is not drawn, and it is the only flag the host acts on for
its *own* behaviour rather than for what it sends. `PlaybackService.StopAsync` waits out the fade
it asked for, so a device that cuts dead has to say so or every stop costs the room that many
seconds of silence before the queue moves on.

Burning anything but the lyrics would mean restarting the encode each time it moved, which is an
audible gap every time a host edits the marquee. So the words are the only thing worth compositing,
and that is a decision the flags let the host make separately rather than all at once.

## What implementing it settled

- **`IScreenServer` survives, wrapped.** `LocalScreenDisplayProvider` holds it rather than replacing it:
  the registration handshake, the MAC and the per-screen stream-URL rewrite are all still its job,
  and the provider is a face over them. There is one way to reach a screen — `LocalScreenDisplayProvider`
  itself, and nothing else holds `IScreenServer` any more. `PlaybackService` drives transport
  through it like any other display; the marquee, QR codes, break music card and next-singer card
  are pulled and sent by the provider on its own, in response to what the broker says moved, never
  pushed by `PlaybackService`. The QR code arrives as data (`IQrCodeService`'s `QrCodeOffer`) and the
  break music card is composed from `IBreakMusicService`; encoding and placement are the provider's. The server registers one
  screen (a constant, not an option) and sends only by `BroadcastCommandAsync`.
- **Switching displays** disconnects whatever was live before connecting the new one, in
  `SettingsButton.SelectDisplayAsync`, and every provider is asked — two displays carrying one song
  is the state the control exists to make unreachable. The "Launch Screen" confirm in
  `DialogService` goes the same way, so a second screen is refused by the provider rather than
  opened beside the first. `ConnectedDisplay.Find` is how the host finds the one provider that is
  connected among the several registered. The selector is a **Quick Settings row in
  the menu**, beside Venue and Theme: the three ask the same question — what is this set to, and
  what else could it be — so they share a shape rather than inventing a control for this one.
- **Searching is a toggle driven by `IsDiscovering`**, never by a flag the UI keeps. Discovery
  outlives the call that starts it (the Cast provider sweeps once, then listens continuously), so
  anything tracking its own "searching" state says so for one sweep and then lies. Stopping has to
  be reachable: a console runs all night on whatever wifi the room has.
- **There are no roles and no sync.** No coordination service, no sync capability, no timeline
  command and no start lead. The one thing that was never about roles is the venue's volume,
  applied on every connect and venue edit by `LocalScreenDisplayProvider`, which is where the screens'
  own housekeeping belongs.
- **`LibraryBreakMusicProvider` routes to the displays**, not to an audio screen. A display that
  cannot take a second channel inherits the no-op defaults and still counts as somewhere the track
  played, or a television that simply cannot carry the bed would suppress the card naming it.
- **`SearchesForDevices`** was added while wiring the selector: "discovery" really is two acts, and
  a console offering a search button has to know which it is about to trigger.

## Questions this should answer without further argument

1. Where does a new overlay go, does it get its own flag, and what happens on a device that cannot
   draw it?
2. What does a plugin author implement, minimally, to send a song to a device?
3. Which component decides a stream needs words burned into it, and what does it ask?
4. Why are lyrics burned when unsupported while the marquee is simply dropped?
5. Why is there no way for a venue to have two displays at once?
