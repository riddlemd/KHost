# Migrating KHost.Screen from Avalonia to Photino

Status: **investigated, not started.** Measured 2026-08-17 on macOS arm64 (Apple M5), .NET 10,
Photino.NET 4.0.16, ffmpeg 8.1.2.

## Verdict

Feasible. Video delivery, transcode cost and cross-platform portability are all settled by
measurement. One windowing issue is unresolved and one platform is unverified.

The driver is the screen UI, not the dependency count: lyrics overlays, next-singer banners and
themes matching the host are hard against a `WriteableBitmap` and close to free in HTML/CSS.
Dropping Avalonia on its own would be a lateral move — it trades an OpenAL P/Invoke layer and a
hand-rolled AVI demuxer for an HTTP server and a JS control layer.

## Target architecture

```
ffmpeg → fragmented MP4 on stdout → Kestrel (loopback) → fetch() ReadableStream in JS
                                                        → SourceBuffer.appendBuffer() → <video>
```

Photino hosts the page. The SignalR contract does not move: `ScreenCommands`, `IScreenClient` and
`ScreenIpcController` are untouched, and Screen stays a **separate process** — `ConnectAsync(serverUri,
screenId, …)` exists so a screen can run on another machine.

Control messages (play/pause/seek/volume/pitch) travel over Photino's C#↔JS bridge, which is
string-only. That is sufficient; media never touches the bridge.

### Why MSE and not `<video src>` or HLS

`<video src>` pointed at a live ffmpeg pipe **cannot work on macOS**. WKWebView refuses any HTTP
resource whose total length is unknown at request time — `MEDIA_ERR_SRC_NOT_SUPPORTED`, 100% of
attempts, across chunked-200, faked-206 and honest-206 responses. The media stack does the fetching
and demands a definite length.

HLS works (205–287ms) but needs `hls.js` on Chromium, which has no native HLS. MSE works on both
engines because JavaScript does the fetching — the media stack is handed bytes and never resolves a
URL, so unknown length stops mattering.

## Measurements

| | WKWebView (macOS) | Chromium (≈ Windows/WebView2) |
|---|---|---|
| Startup median | 259ms | **124ms** |
| Seek median | 276ms | **130ms** |
| `waiting` / `stalled` | 0 / 0 | 0 / 0 |
| Quota errors over 60s | 0 | 0 |
| `currentTime` after seek to 30s | 30.55s | 30.76s |

Baselines: HLS 205–287ms · static file 111ms · **current native pipeline ~20–35ms**.

MSE is 4–10x slower to start than the pipeline it replaces, and comfortably under the threshold
where a human notices. Chromium — the primary venue platform — is the faster engine.

`currentTime` reporting true absolute position is a correctness result, not a performance one: it
feeds `ScreenPlaybackState.Position` back to the host. Playback that looks fine while reporting the
wrong position is the failure mode to watch for.

### Transcode cost is not a constraint

Encoding 30s clips, `speed=` multiplier:

| Input | fMP4 encode | rawvideo (today) |
|---|---|---|
| 1080p MP4 | 28.7x | 56.3x |
| 1080p AVI (mpeg4) | 30.5x | 77.8x |
| CDG-sized 300×216 | 224x | 541x |
| 1080p MP4, `-c:v copy` | **271x** | — |
| 1080p AVI, `-threads 2` | **13.7x** | 81.2x |

Realtime-safe with wide margin even pinned to two threads. Measured on an M5, so treat as best case;
extrapolating a 5–10x per-core penalty for a budget x86 venue PC still lands near 1.4–2.7x.

`.cdg`, `.avi` and `.flv` are not stream-copyable and always pay the encode. `.mp4`/H.264 sources
should be routed through `-c:v copy` — `MediaInfo` already carries what is needed to decide.

## The configuration that works

```
-f mp4 -movflags frag_keyframe+empty_moov+default_base_moof
-c:v libx264 -preset ultrafast -tune zerolatency -profile:v baseline -level 3.1
-pix_fmt yuv420p -g 30 -keyint_min 30 -sc_threshold 0 -bf 0
-c:a aac -ar 44100 -ac 2 pipe:1
```

MIME: `video/mp4; codecs="avc1.42E01F,mp4a.40.2"` — Constrained Baseline 3.1, confirmed with ffprobe
against real output rather than assumed. A mismatch makes `addSourceBuffer`/`appendBuffer` throw, so
derive this string from the encoder settings; do not hardcode a guess.

`-preset ultrafast -tune zerolatency` is load-bearing: the default preset drops 1080p from 30.5x to
8.25x. Do not let it get tidied away.

## Traps

**The playhead must be moved into the buffered range after a seek.** Setting
`sourceBuffer.timestampOffset = offset` places data at absolute time, but `video.load()` leaves the
playhead at 0, so the element waits forever for data that will never arrive. Offset 0 works and every
non-zero offset hangs — 0/5 seeks succeeded before this fix, 5/5 after:

```js
if (!seeded && sourceBuffer.buffered.length > 0) {
    seeded = true;
    const start = sourceBuffer.buffered.start(0);
    if (video.currentTime < start) video.currentTime = start;
    video.play();
}
```

**`appendBuffer` needs a queue.** It is async; appending before `updateend` throws `InvalidStateError`.

**Evict old buffered ranges** or a full-length song hits `QuotaExceededError`. `remove(0, currentTime - 20)`
while not updating was enough for a clean 60s run.

**MPEG-TS is unusable.** MSE does not accept it — fragmented MP4 only. It was also slower than fMP4
under HLS and produced a false-positive `playing` event with `currentTime` frozen at 0.

## What changes

| File | Lines | Fate |
|---|---|---|
| `DefaultMediaPlayer.cs` | 590 | ffmpeg orchestration, seek-by-restart, pitch, fade and state survive; frame decode and demux go |
| `Views/MainWindow.axaml` + `.cs` | 487 | deleted → HTML page + thin Photino host |
| `OpenAl/OpenAlAudioPlayer.cs`, `OpenAlNative.cs` | 358 | **deleted** — the browser owns audio |
| `FFmpeg/AviDemuxer.cs` | 136 | **deleted** |
| `ScreenIpcController.cs` | 117 | unchanged |
| `App.axaml.cs`, `Program.cs` | 132 | Photino equivalents |

Roughly 980 lines deleted, ~350 written (loopback static/stream server, player page and JS, Photino
host). The core change is the argument string in `BeginSegment` (`DefaultMediaPlayer.cs:256-295`) —
the CDG output-`-ss` handling and the `asetrate/atempo` pitch filter are unaffected, because ffmpeg
already normalises CDG to a video stream and already applies pitch server-side.

`IMediaPlayer` survives as the control surface. Only `FrameAvailable`/`FrameData` are removed, and
they are used nowhere outside `KHost.Screen`.

## Open risks

**Fullscreen.** `SetFullScreen(true)` is a silent no-op on macOS — verified across unbundled, bundled
and ad-hoc-signed builds, with the DLL decompiled to confirm the getter really reaches native. It is
not a wrapper bug and packaging does not fix it.

The workaround is a chromeless window positioned and sized to the monitor's `MonitorArea`.
Chromeless, positioning and topmost all work. But macOS clamps Y from 0 to 34 to protect the menu
bar, leaving a visible strip and pushing 34px of content off the bottom. **Measured on the primary
display only** — a projector is a secondary display, which may have no menu bar depending on
"Displays have separate Spaces". Untested: this machine has one display.

If the strip does appear on secondary displays, the proper fix is
`NSApplication.presentationOptions` with `.autoHideMenuBar`, which Photino does not wrap.

**Linux.** WebKitGTK routes media through GStreamer, so H.264/AAC decode depends on which
`gst-plugins-*` the distro ships. A packaging dependency, not a code problem.

**Real media.** Everything above used synthetic `testsrc2`/`sine` sources. CDG has never been through
the MSE pipeline.

## Sequencing

1. **Settle fullscreen on a second display** — two minutes with a monitor plugged in, and it decides
   whether a native `presentationOptions` call is needed before anything else is worth building.
2. **Player page and loopback server** against real karaoke media, including a CDG pair.
3. **Swap `BeginSegment` output** to fMP4 with copy-vs-encode routing from `MediaInfo`.
4. **Port the control surface** — map `ScreenCommands` onto the JS player over the Photino bridge.
5. **Delete** OpenAL, `AviDemuxer`, `MainWindow`.
6. **Verify on Windows and Linux.**

Steps 2–5 are roughly 2–4 days. Step 1 gates the rest; step 6 is where the remaining unknowns live.
