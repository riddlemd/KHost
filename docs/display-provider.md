# What an `IDisplayProvider` is

**Implemented.** `IDisplayProvider` is in `KHost.Abstractions/Services/`, the screens reach it
through `KHost.Domain/Services/Displays/LocalScreen/LocalScreenDisplayProvider.cs`, and `PlaybackService` drives
whichever one is connected through one dispatcher. The interface itself is the authority on the current
surface; this note keeps the reasoning behind its shape.

> A display provider owns **a transport to places the song comes out**. It finds such places,
> connects to one, hands it what to play and drives transport on it. What it draws there is its own
> business, from data the host publishes. It does not decide *what* the show is — it is told.

## Why it exists

`IDisplayProvider` arrived narrowly, to get Chromecast out of the host. A screen was driven through
something else entirely: `IScreenServer`, twenty commands, its own registration handshake, roles and
sync. Two kinds of output with no shared vocabulary.

That cost something real. When drawing lyrics moved off the encode and onto the display for a format
that ships separate stems, the words went out as `SetTimedLyricsCommand` — a *screen* command — so a
connected Chromecast silently played a stream with no words on it, and that stems-only format with
no picture at all. Nothing in the design caught it, because nothing in the design said these were
two of the same thing.

A screen is not a plugin. But the screens should reach the host **through a provider**, so there is
one answer to "where does the song come out, and what can it show?"

## The two implementations

| | `LocalScreenDisplayProvider` (core) | `ChromecastDisplayProvider` (plugin) |
|---|---|---|
| Transport | SignalR IPC over loopback | CASTV2 across the LAN |
| Reach | same machine only | off-box |
| Finding devices | launching one — the host starts the process | mDNS browse |
| Draws | everything, from the host's public read-sides | nothing; asks for burned-in words |

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
    string Name { get; }
    event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged;

    // --- discovery ---
    bool IsDiscovering { get; }
    bool SearchesForDevices => true;
    Task StartDiscoveryAsync(CancellationToken ct = default);
    Task StopDiscoveryAsync(CancellationToken ct = default);
    IReadOnlyList<DisplayDevice> Devices { get; }

    // --- connection: one device, which every argument-free member addresses ---
    string? ConnectedDeviceId { get; }
    Guid? SessionId { get; }
    Task<bool> ConnectAsync(string deviceId, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    // --- transport and control ---
    RenderTarget DescribeTarget() => RenderTarget.None;
    Task LoadAsync(DisplayLoad load, CancellationToken ct = default);
    Task PlayAsync(CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task StopAsync(TimeSpan? fade = null, CancellationToken ct = default);
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);
    Task SetVolumeAsync(float volume, CancellationToken ct = default) => Task.CompletedTask;
    Task<bool> SetStemVolumeAsync(StemLevel level, CancellationToken ct = default) => Task.FromResult(false);

    // --- the second audio channel ---
    Task LoadBackgroundAsync(BackgroundLoad background, CancellationToken ct = default) => Task.CompletedTask;
    Task PlayBackgroundAsync(CancellationToken ct = default) => Task.CompletedTask;
    Task PauseBackgroundAsync(CancellationToken ct = default) => Task.CompletedTask;
    Task StopBackgroundAsync(TimeSpan? fade = null, CancellationToken ct = default) => Task.CompletedTask;
    Task SetBackgroundVolumeAsync(float volume, CancellationToken ct = default) => Task.CompletedTask;
}
```

**Nothing drawn is on it.** The drawing members it once had — words, marquee, codes, cards,
pictures — had no host caller once every overlay became something the provider pulls for itself,
so they went, along with the `PlaybackService` dispatcher that turned the local screen's wire
commands into calls. A provider
that draws reads `IPlaybackService.CurrentProgram`, `IUpNextService`, `IQrCodeOfferService`,
`NextSingerAnnounced`, `IBreakMusicService`, `ITimedLyricsService` and the venue's settings, each
with the message that says it moved.

**Its arguments are models, not a wire.** `DisplayLoad`, `StemLevel` and `BackgroundLoad` live in
`Abstractions`; the local screen's commands live in `KHost.IPC.SignalR.Contracts`, which no plugin
sees, and `LocalScreenDisplayProvider` maps one onto the other.

There is deliberately **no timeline or sync member, and no device count**. One display, full stop:
the local screen or a receiver, never both and never two of either. The display that is up defines
the song's clock and nothing is steered onto anything else.

## What a device carries

`DisplayDevice` is the row in the console — id, name, model, address, whether it is the connected
one — and three facts about sound and picture: `SupportsAudio`, `SupportsVideo` and
`SupportsFade`.

`SupportsFade` is the one the host acts on for its *own* behaviour. `PlaybackService.StopAsync`
waits out the fade it asked for, so a device that cuts dead has to say so or every stop costs the
room that many seconds of silence before the queue moves on.

It once carried a flag per overlay and one for mixing stems. With drawing the provider's own
business the overlay flags had nothing to gate, and what to render moved to `DescribeTarget()`,
which the provider answers for the device it is actually connected to.

### Why only the words are burned in

Lyrics are fixed for the whole song — every syllable is known at load — so a display that cannot
draw them asks for `RenderTarget.BurnLyrics`, and a renderer able to burn them in composes the
picture. The marquee rescrolls on a venue edit, QR codes move, cards appear on a host's action:
burning any of those in would restart the encode each time, an audible gap. A display that cannot
draw them simply does not.

## What implementing it settled

- **`IScreenServer` survives, wrapped.** `LocalScreenDisplayProvider` holds it rather than replacing it:
  the registration handshake, the MAC and the per-screen stream-URL rewrite are all still its job,
  and the provider is a face over them. There is one way to reach a screen — `LocalScreenDisplayProvider`
  itself, and nothing else holds `IScreenServer` any more. `PlaybackService` drives transport
  through it like any other display; the marquee, QR codes, break music card and next-singer card
  are pulled and sent by the provider on its own, in response to what the broker says moved, never
  pushed by `PlaybackService`. Everything it draws from is public, so a plugin display can do the
  same: the QR code arrives as data (`IQrCodeOfferService`'s `QrCodeOffer`), the singers from
  `IUpNextService`, the next-singer card as `NextSingerAnnounced`, the picture from
  `IPlaybackService.CurrentProgram`, and the break music card is composed from
  `IBreakMusicService`; encoding and placement are the provider's. The server registers one
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
  cannot take a second channel keeps the no-op defaults and still counts as somewhere the track
  played, or a television that simply cannot carry the bed would suppress the card naming it.
- **`SearchesForDevices`** was added while wiring the selector: "discovery" really is two acts, and
  a console offering a search button has to know which it is about to trigger.

## What to render is the display's to say

`IDisplayProvider.DescribeTarget()` answers the `RenderTarget` the host hands every renderer, asked
of the connected provider on each load and each reconnect. Its default body is `RenderTarget.None`:
one mixed stream, no burned-in words, which is all a transport-only provider can play.
`RenderTarget.MixesStems` commits a provider to playing `DisplayLoad.Stems` and answering
`SetStemVolumeAsync`; when that answers false, the host rebuilds the stream at the playhead with the
new mix instead. `RenderTarget.BurnLyrics` is a request a renderer may honour. The local screen
mixes and draws its own words, so it asks for stems, with nothing burned in.

## Questions this should answer without further argument

1. Where does a new overlay go, and what happens on a device that cannot draw it?
2. What does a plugin author implement, minimally, to send a song to a device?
3. Which component decides a stream needs words burned in, and what does it ask?
4. Why are lyrics burned in when unsupported while the marquee is simply dropped?
5. Why is there no way for a venue to have two displays at once?
