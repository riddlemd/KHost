# Spike: move ffmpeg and streaming into the host

**Question.** If the main KHost app owned the ffmpeg transcode and served the result over HTTP,
turning the screen into a plain consumer, would that also let us stream to other things —
Chromecast in particular?

**Verdict.** Yes, and the Chromecast part is the *cheap* part. The stream a host-side transcode
naturally produces is already Cast-compliant and LAN-reachable; only the CASTV2 control handshake
is left, and mature .NET libraries cover it. The stronger argument for the move is unrelated to
casting: it deletes the shared-filesystem requirement and roughly all of Screen2's media plumbing.

The spike also surfaced one non-obvious defect that would have shipped (`EXT-X-START`, below) and
one design consequence that is genuinely awkward (the position clock).

Status: spike only. Nothing here is wired into `KHost.slnx`, and nothing in `src/` was changed.

---

## What was built

`spikes/host-streaming/` — two throwaway projects, deliberately outside the solution so they
cannot affect the build.

| Project | Stands in for | Notes |
|---|---|---|
| `KHost.Spike.StreamHost` | the main app owning ffmpeg | Minimal API on `0.0.0.0:5480`. `POST /api/session` starts a transcode, returns one playlist URL. Serves HLS with CORS, byte-range, and no-cache playlists. |
| `KHost.Spike.ScreenConsumer` | what Screen2 becomes | A Photino window and a `<video src>`. No ffmpeg, no MediaSource, no access to the media file. ~110 lines including the page. |

Run it:

```bash
cd spikes/host-streaming/KHost.Spike.StreamHost && dotnet run
# then, with the playlistUrl from POST /api/session:
cd spikes/host-streaming/KHost.Spike.ScreenConsumer && dotnet run -- --playlist <url>
```

Or open `http://<lan-ip>:5480/` for a browser consumer and a session list.

---

## Measured

All on this machine, ffmpeg 8.1.2, a synthetic 4-minute 720p30 H.264/AAC source.

| Measurement | Result |
|---|---|
| `POST /api/session` returns | 16 ms |
| Stream first playable | 129 ms |
| Consumer process launch → first frame | 0.80 s (includes .NET and window startup) |
| Full 4-min song transcoded to HLS | 7.5 s wall, 55 s CPU — **32× realtime** |
| 3 concurrent consumers on one session | 0 extra transcodes, ~2.1 s each to fully demux |
| Seek fidelity | offset 90 s on a 240 s source → stream duration exactly 150 s |
| Segment codecs | H.264 **Main@L4.1**, AAC-**LC** 44.1 kHz stereo, MPEG-TS |
| Playlist/segment HTTP | `206 Partial Content` with correct `Content-Range`, `Access-Control-Allow-Origin: *`, `Cache-Control: no-cache` |

CPU, same workload, three screens:

| Model | Wall | CPU |
|---|---|---|
| Today — 3 screens each running their own ffmpeg | 10.7 s | **95.5 s** |
| Spike — 1 shared transcode, consumers only demux | 7.8 s | **58.3 s** |

Break-even is about two screens. Below that the shared model is slightly *worse*, because the
spike encodes at `veryfast`/Main for compatibility where Screen2 uses `ultrafast`/baseline. Above
it the saving is linear: today's cost is N transcodes, the spike's is always one.

---

## Chromecast

**Proven here.** The stream is inside the support matrix of every Chromecast generation
(H.264 up to High@L4.1, AAC-LC, HLS with MPEG-TS segments — TS rather than fMP4/CMAF deliberately,
since CMAF needs a newer receiver). It is served on a routable LAN address, with the CORS header a
receiver requires and the byte-range support it uses to fetch segments. A Cast device would be
handed the exact same URL the local screen plays.

**Not proven here.** The CASTV2 control path — mDNS discovery, launching the receiver app, the
`LOAD` message. There is no Chromecast or AirPlay device on this network (`dns-sd -B
_googlecast._tcp` returns nothing), so this is untested, not merely unimplemented. The spike
exposes `GET /api/cast/discover` which shells out to `dns-sd`; run it on a network with a device
to get past this point. `Sharpcaster` (3.0.0, actively maintained) covers discovery through
`LOAD`; `GoogleCast` (1.7.0) is the alternative. Low technical risk, but unverified.

**The awkward part.** A Chromecast will never connect back over SignalR, so it cannot be an
`IScreenClient`. The current `IScreenProvider` / `IScreenServer` split assumes screens dial in and
receive commands. A Cast target inverts that: the host must push to it. Casting therefore needs a
second control adapter alongside the SignalR one, not just a new `IScreenProvider`.

---

## Two things the spike caught

**1. Players join at the live edge and skip the start of the song.**

Because the transcode runs ~32× realtime, by the time a player attaches the growing `EVENT`
playlist already holds a minute of segments, and it has no `ENDLIST`. Safari treated it as a live
stream and started **16.6 seconds into the song**. For karaoke that is fatal and it would not have
shown up in any unit test.

Fixed by having the host inject `#EXT-X-START:TIME-OFFSET=0,PRECISE=YES` when it serves the
playlist. Re-measured: playback starts at 0.6 s. The host rewriting the playlist on the way out is
the right layer for this — ffmpeg will not emit the tag.

**2. Duration is unknown while the song is still transcoding.**

No `ENDLIST` means players report `duration: 0`. Not a problem in practice — `Media.Duration` is
already in the database and is more trustworthy than the player anyway — but any UI reading
duration from the screen would break.

---

## Consequences for the current design

**Good: the shared-filesystem requirement disappears.** `LoadMediaCommand` carries a `FilePath`
today, so every screen must be able to open the library itself — fine for a local screen, a real
constraint for anything remote. Moving to a URL removes it entirely. This is the single biggest
win and it has nothing to do with casting.

**Good: Screen2 loses almost all of its complexity.** `MediaStreamServer` (200 lines), the
`BrowserMediaPlayer`/MSE machinery, `player.js`'s MediaSource pump, eviction, and quota handling
(230 lines) all collapse into `<video src="…m3u8">`. The consumer spike is the whole client.

**Bad: Chromium has no native HLS.** WKWebView on macOS plays HLS from a bare `src` — verified.
WebView2 on Windows does not, and would need `hls.js` bundled. So the client is not quite free
cross-platform, though `hls.js` is still far less code than what it replaces.

**Bad, and the real design problem: the position clock.** `PlaybackService.Position` is an
authoritative local wall clock — a 500 ms timer the host advances itself. That is correct only
while the output has negligible buffer. A Chromecast sits several seconds behind, with its own
clock, and cannot be made to follow the host's. With a network player as an output, `Position`
has to become a *follower* of whichever output is designated clock master, rather than a timer.
That is a real change to a class that was just hardened around the opposite assumption.

**Bad: two audio outputs in one room is unusable.** A local screen buffers ~0.5 s; a Cast device
several seconds. Any design where both play audio is a non-starter. The workable rule is exactly
one audio output at a time, with other consumers either muted or accepted as delayed mirrors.

**Also:** binding the media endpoint to `0.0.0.0` exposes the library to the LAN. The spike uses
unguessable 8-hex session ids and blocks path traversal, but a real implementation wants a
per-session token. Segment directories also need lifecycle management — the spike deletes on
session dispose and on shutdown, which is not enough for a long night.

---

## Recommendation

Worth doing, sequenced so that casting is last and optional:

1. ✅ Move ffmpeg and HLS generation into the host behind an `IMediaStreamService`.
2. ✅ Give `LoadMediaCommand` a stream URL. Done additively rather than as a replacement — see below.
3. ✅ Strip Screen2 to the consumer shape.
4. ✅ Make `PlaybackService.Position` follow the timing reference instead of a local timer.
5. ✅ Casting, as a transport rather than an `IScreenProvider`.

---

## Casting — landed

A Cast receiver is not an `IScreenServer` connection and never will be: it does not dial in over
SignalR. So `IScreenServer` became a **composite over `IScreenTransport`s** — SignalR is one, the
Cast sender is another — and everything above it is unchanged. `PlaybackService`, the coordination
service and the screens page never learned what a Chromecast is.

| Piece | Where |
|---|---|
| `IScreenTransport` | `KHost.Abstractions` — enumerate, send, events; `SendCommandAsync` returns false for "not mine" |
| `CompositeScreenServer` | `KHost.Domain` — routes per screen, broadcasts per connection |
| `ScreenServerService` | now an `IScreenTransport` rather than the `IScreenServer` |
| `CastScreenTransport` | `KHost.Cast`, on Sharpcaster 3.0.0 |

**Discovery is not attachment.** Every Chromecast on the network is listed; taking one over
uninvited would hijack whatever the household is watching. The screens page lists them and the user
presses Use, which connects, launches `CC1AD845`, and publishes the device as a screen. Casting is
off by default (`Cast:Enabled`) because discovery browses the network.

**A Cast device declares `SupportsAudio` and `SupportsVideo` but not `SupportsSync`**, so the role
split does the rest on its own: it can hold the audio role, never the timing one, and it is muted
by default like any other non-audio screen.

### Things that bit, and where they are handled

- **The host resolves its own base address to `localhost`.** On a television that means the
  television. `CastScreenTransport.MakeReachableFromDevice` swaps in a LAN address and leaves an
  already-routable one alone.
- **`MediaChannel.SetVolumeAsync` is unusable** — Sharpcaster throws `MediaSessionID is not
  available` even with a live session. Muting goes through `ReceiverChannel.SetMute`, which is
  device-wide rather than scoped to our stream. A fixed-volume receiver can refuse outright; that
  is the device declining, and the transport logs it rather than pretending.
- **`StopCommand`'s fade is dropped.** Cast has no volume ramp, and faking one would move the TV's
  own level.
- **`SampledAtUtc` is deliberately null** on Cast state reports, so the host cannot accidentally
  anchor the group on a clock it has no business trusting.

### Verified

`tests/KHost.UnitTests/Cast/` drives the emulator at `~/Developer/riddlemd/Chromecast-Emulator`
for real — discovery, attach, load, play, position reports, mute, detach, and the transport
routing and role assignment on top. They skip when nothing is listening on `127.0.0.1:8009`.

Still open: nothing casts a *fade*, pitch still needs host-side restreaming, and the emulator
cannot exercise the one thing that most distinguishes a real device — the Eureka certificate
chain, which stock senders verify and this one cannot present.

Steps 1–3 stand on their own merits even if casting is never built.

---

## Transition status — steps 1–3 landed

| Piece | Where |
|---|---|
| `IMediaStreamService` / `MediaStreamSession` | `KHost.Abstractions` |
| `HlsMediaStreamService` | `KHost.Domain/Services` — singleton, `SemaphoreSlim`-guarded, registered in `AddDomain()` |
| `/media/{sessionId}/{fileName}` | `KHost.UserInterface/Endpoints/MediaStreamEndpoints.cs`, injects `EXT-X-START` on the way out |
| Stream lifecycle | `PlaybackService` — opens on load, reuses for reconnects, closes in `EndedAsync` |
| `StreamMediaPlayer` | `KHost.Screen2`, replacing `BrowserMediaPlayer` |

Deleted: `KHost.Screen2/MediaStreamServer.cs` (200 lines), `BrowserMediaPlayer.cs` (200 lines), and
the MediaSource pump in `player.js`. Screen2 no longer references FFMpegCore and no longer runs a
loopback web server — it loads its page from a local file and plays a URL.

**`LoadMediaCommand` gained `StreamUrl` and `StreamStartOffset` rather than losing `FilePath`.**
KHost.Screen (Avalonia) is still in the solution, still copied to the UI's output, and still
decodes locally from a path. Replacing `FilePath` would have broken it silently, so both screens
are served by one broadcast: Screen2 takes `StreamUrl`, KHost.Screen takes `FilePath`. `FilePath`
becomes removable the day KHost.Screen goes.

**Seeking no longer restarts a transcode.** It used to tear down ffmpeg and start a new one at the
offset. The screen now just moves `video.currentTime` inside the stream it already holds.

**Pitch is the one regression.** It was an ffmpeg filter applied on the screen, and ffmpeg is no
longer there. `HlsMediaStreamService` builds the filter and `StreamMediaPlayer` logs a warning
instead of applying it, so a pitch change needs the host to open a new stream and re-issue the
load. Nothing currently sends `SetPitchCommand` — no caller exists in the UI — so nothing
regressed in practice, but wiring pitch up now requires host work rather than screen work.

**`ResolveBaseAddress` normalises wildcards to `localhost`.** Fine for screens on the host machine;
a Chromecast needs a LAN-routable address, so step 5 will have to resolve a real interface address
(the spike's `ResolveLanAddress` shows the shape) or take one from `MediaStream:BaseAddress`.

---

## Multi-screen sync — measured, and not solved by either design

Two screens consuming one stream, started 6 s apart:

```
t=…316  screen1= 6.645  screen2= 0.591  skew=6.053s
t=…321  screen1=11.666  screen2= 5.595  skew=6.070s
SKEW: min=6.053s max=6.074s
```

The skew is the start stagger, it **never converges**, and it drifts a further ~1 ms/s (~0.1 %
clock difference). Nothing pulls the screens together because nothing ever tried to: the previous
design had no shared timeline either — each screen ran its own ffmpeg and started when its own
transcode spun up. This is a pre-existing hole that host-side streaming makes *fixable* rather than
one it introduces, since all screens now share one timeline with common timestamps.

Note the tension with `EXT-X-START:TIME-OFFSET=0`: it is what stops a late joiner skipping the top
of the song, and it is also what makes a late joiner start 6 s behind everyone else. Both cannot be
right. Under a scheduled start the late joiner must seek to where the group is; the tag stays as
the default only for loose consumers.

### What synchronising actually requires

1. **A shared time base.** `-hls_flags +program_date_time` stamps absolute time into the playlist —
   the standard HLS mechanism for this — plus an NTP-style offset handshake over the existing
   SignalR channel, because each screen's OS clock differs.
2. **A scheduled start.** Replace "play now" with "present content-time X at wall-clock T".
3. **Continuous correction** on each screen: under ~40 ms do nothing; 40–500 ms slew via
   `playbackRate` 0.98/1.02, which is imperceptible; over 500 ms hard seek. This is how Snapcast,
   AirPlay 2 and Sonos handle multiroom.
4. **Audio from exactly one output.** 20 ms between two speakers in a room is comb filtering and
   100 ms is a slapback echo. Video tolerates 50 ms skew; audio does not.

### The hard limit

A Chromecast cannot join a synced group. The Cast protocol offers `LOAD`/`PLAY`/`SEEK` and a coarse
position report — no fine rate control, and a buffer the sender does not own. So the model has to be
an explicit split: a **synced group** of local screens, plus **loose consumers** (Cast, browser,
smart TV) that are allowed to lag by design.

### Sync group — built and measured

Screens declare `SupportsSync` when they register (`ScreenHub.RegisterScreenAsync`), the host keeps
it on `IScreenConnection.Capabilities`, and `SetTimelineCommand` is addressed only to screens that
declared it. KHost.Screen (Avalonia) registers as non-capable; a Cast device would too.

Measured with two real Screen2 processes started **6 s apart**, driven by the real IPC hub and the
real `HlsMediaStreamService` (`spikes/host-streaming/KHost.Spike.SyncHarness`):

```
before:  skew=6.053s … 6.074s   (never converges)
after:   skew=0.048s at t=96s   (stable)
```

Four bugs surfaced only by running it, none of which any unit test would have caught:

1. **The host advertised a playlist URL before ffmpeg had written one.** A media element reports a
   404 playlist as `MEDIA_ERR_SRC_NOT_SUPPORTED` and never retries, so the screen died silently.
   `OpenAsync` now waits for a playlist that names a segment before returning.
2. **`long` was missing from `ScreenCommandJsonContext`.** That context is the only resolver in
   SignalR's chain, so the clock echo's return value could not serialize and the hub aborted the
   connection mid-handshake. `ScreenHubContractTests` now fails the build for any hub payload type
   that is not registered.
3. **`requestAnimationFrame` is throttled while a window is occluded**, which froze the correction
   loop exactly when a screen was most likely to have drifted. It runs on `setInterval` now.
4. **Repeated hard seeks starve playback.** Each seek costs a segment refetch and decoder reset,
   leaving the screen further behind, which triggers another seek. Under a seek-per-tick policy the
   screens sat 0.53 s behind with constant rebuffering. Each timeline now gets exactly one
   alignment seek; everything after is rate trimming.

### Rate must be exactly 1.0 — and now is

A karaoke screen cannot carry a permanent rate trim: +4% is roughly 0.7 semitones sharp. The first
working version held the screens together at the cost of pinning `playbackRate` at 1.04 forever,
which is not a solution for a music app. Two changes fixed it.

**The host stopped asserting a timeline and started reporting one.** `PlaybackService` subscribes to
`StateReceived` and re-anchors the group onto the position the primary has actually reached, using
the primary's own `SampledAtUtc` stamp so no delivery latency is guessed at. `Position` follows the
primary rather than free-running — the local timer is only an interpolator between reports now,
which is the clock-master change finally landing.

**Rate trimming was removed entirely.** Telemetry showed WKWebView walking `currentTime`
*backwards* whenever the rate was off 1.0, so the trim was destabilising the thing it was meant to
settle — that is the sawtooth in the earlier numbers. Left at 1.0, two screens hold a constant
offset to about 1 ms/s (visible in the very first measurement: 6.053 → 6.074 over 21 s), so one
accurate alignment lasts minutes. Correction is now a seek, gated behind three confirmations at
150 ms, and nothing else.

Measured, two screens started 6 s apart, over 100 s:

```
skew        6.053s  ->  0.001s   (stable, both screens)
rate        1.0400  ->  1.0000   (min == max == 1.0000 on both)
error       -0.53s  ->  ±0.002s  against the group timeline
```

### Capabilities are declared, not inferred

A screen states what it is when it registers — `SupportsSync`, `SupportsAudio`, `SupportsVideo`:

| Screen | sync | audio | video |
|---|---|---|---|
| KHost.Screen2 | ✅ | ✅ | ✅ |
| KHost.Screen (Avalonia) | — | ✅ | ✅ |
| `ScreenCapabilities.CastDevice` | — | ✅ | ✅ |
| `ScreenCapabilities.None` | — | — | — |

Audio and video are separate flags because the primary may be an audio-only output, leaving the
lyrics displays following it as the only things rendering video. The primary is elected from the
audio-capable members of the synced group and holds the role for the life of the song.

### This supersedes "clock master"

Step 4 as originally written — `Position` follows one designated output — is the right answer for a
single buffered output and the wrong shape once screens must agree with each other. Correct model:
**the host owns the timeline.** `Position` derives from the schedule the host published (corrected
once for actual group start), and screens report their error against it for correction and
monitoring rather than defining it.

### Verified

- 547 unit tests pass; solution builds clean.
- `HlsMediaStreamServiceTranscodeTests` drives **real ffmpeg**: playlist and segments appear,
  `CloseAsync` kills the transcode and deletes its directory, two sessions stay independent.
  Skipped automatically where ffmpeg is absent.
- Real host, headless: `Media stream base address resolved to http://localhost:5251`, `/media`
  returns 404 for unknown sessions and for traversal attempts, app root serves 200.
- Real Screen2, stripped: connects over SignalR to the running host, loads its page from disk with
  no media server, and its JavaScript is live (the double-click fullscreen gesture round-trips).
