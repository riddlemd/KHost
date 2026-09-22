# Playing a `.kit` natively

Why the screen draws a song's words itself, and what is still unanswered about it.

The question: instead of rendering a `.kit` to mp4 with SkiaSharp and ffmpeg and then streaming
that back as HLS, could a screen decode the stems and draw the lyrics itself?

The numbers below came from a throwaway `KHost.Screen3` probe, which has served its purpose and
been deleted. Its measurements are kept here because the engine's design rests on them.

**Since this was written, the host-side render path has been removed.** `IMediaPreparer`,
`PreparedMediaService`, `PerformancePreparation` and the whole stream-copy contract
(`CopyPlan`, `CutsCleanly`, `CanCopyVideo`, `CanCopyAudio`, `KeyframeSecondsFor`) are gone:
benchmarked against plain streaming, pre-rendering bought ~0.1–0.2s of start latency and no
reliable CPU saving, and it existed mainly for `.kit`. So a `.kit` is **unplayable today** — the
native path below is not an optimisation any more, it is the only way back. Where the sections
below say a native path would "skip" that machinery, read it as already skipped for everyone.

**This is a second render path, not a replacement.** ffmpeg stays: CDG + mp3 and mp4 (YouTube) have
no native path and are not going to get one. So the question is narrower and safer than it first
looks — whether Screen2 gains a path it takes for files it can open itself, falling back to the
existing stream for everything else. Nothing below proposes deleting the ffmpeg pipeline, and
several of the hard parts stop being hard once it is understood to still be there.

## What it costs today

One 6:02 song, measured on this machine (i7-1250U, 12 threads):

| | |
|---|---|
| Render, no background | 41.6s |
| Render, venue background | **84.1s** |
| Render output | 23.7 MB |
| The `.kit` it was made from | **8.7 MB** |

The render is 2.7x larger than its own source, and takes longer than a third of the song's
length to produce. A background loop doubles it, and almost all of that is ffmpeg's filter graph
— decode, scale, crop, overlay — not our rasterizer, which only rises 11%.

## What is actually in a `.kit`

Measured by dumping one through `KitContainer.Read().Demux()`:

| stream | kind | bytes | share |
|---|---|---|---|
| id 2 instrumental | Ogg Vorbis 44.1k stereo | 4,142,273 | 47.8% |
| id 3 backing vocal | Ogg Vorbis 44.1k stereo | 1,850,257 | 21.3% |
| id 4 lead vocal | Ogg Vorbis 44.1k stereo | 1,811,629 | 20.9% |
| id 1 timing | XML | 500,774 | 5.8% |
| ids 6–11 | jpeg/png | ~262,000 | 3.0% |
| ids 5, 12–20 | unidentified | ~94,000 | 1.1% |

Timing: 57 pages, 482 syllables, 362.1s. Geometry is an abstract ~640x360 space scaled to the
output height; font size is derived (`lineHeight * 0.82`), not stored. The chase is plain linear
`clamp((t - start) / (end - start))` with no easing, drawn as a clip-rect reveal over already
drawn inactive text.

Note there are more image and unknown chunks than the code comments describe (they name id 9 as
"a PNG cover"; this file has jpeg/png at 6, 7, 8, 9, 10 and 11). Worth identifying before
anything depends on them.

## What the webview can do

Measured inside Photino's own webview rather than a desktop browser, with a real Vorbis stem cut
from the kit above. WebView2 on this machine:

| | |
|---|---|
| `decodeAudioData` on real Vorbis | **works** — 2ch, 48 kHz, 3.011s |
| Canvas2D karaoke frame, 1280x720 | 0.095 ms (~10,500 fps) |
| Canvas2D karaoke frame, 1920x1080 | 0.082 ms (~12,100 fps) |
| WebGL2 / OffscreenCanvas / WASM | yes |
| **AudioWorklet** | **yes** — but only once the page has an origin (below) |

Two of these matter more than the rest.

**Vorbis decodes.** This was the risk that could have ended the idea: `canPlayType` answers
"probably" for codecs that then fail on real data, and WebView2 is not Chrome. It decoded an
actual stem. The stems can be handed to the screen as-is.

**AudioWorklet was missing for a reason that had nothing to do with WebView2.** The screen loads
its page with `LoadRawString`, which lands on `about:blank` — origin `null`, and *not a secure
context*. `AudioWorklet` is one of the constructors that is simply not defined there, so the first
pass measured the loader rather than the engine. Re-measured in the same webview, same machine, by
changing only how the page arrives:

| page loaded as | `isSecureContext` | `AudioWorklet` | `addModule` (blob: / data:) |
|---|---|---|---|
| `LoadRawString` — `about:blank` | no | `undefined` | — |
| `http://localhost` | yes | `function` | works |
| `file://` | yes | `function` | works, both |

So a phase vocoder is back on the table, and question 1 below is open again rather than closed by
the platform. The screen ships no files for a worklet module to be served from, but it does not
need to: `addModule` accepts a `blob:` or `data:` URL built from a string the page already carries.

**And the worklet is not the only route.** Inlining more script was never the constraint — the page
already arrives with hls.js, the lyrics overlay and `player.js` inlined into it, and a worklet is a
capability the engine withholds, not a library that could be shipped alongside them. What the
opaque origin actually costs is only the secure-context-gated list. Measured on `about:blank`, with
`file://` alongside for contrast:

| | `about:blank` | `file://` |
|---|---|---|
| `AudioWorklet` | undefined | function |
| `ScriptProcessorNode` | **fires** — 3 callbacks, 12288 frames in 1s | fires |
| `WebAssembly` compile | works | works |
| `OfflineAudioContext` render | works | works |
| WebCodecs (`AudioDecoder`) | undefined | function |
| `crypto.subtle` | absent | present |
| `localStorage` | `SecurityError` | works |

So a vocoder could be built today, unchanged loader and all, either on the deprecated main-thread
`ScriptProcessorNode` or by shifting the stem offline into an `AudioBuffer` before it plays —
the stems are decoded whole into memory anyway. The worklet buys the audio render thread, which is
what keeps a vocoder from glitching under a Blazor repaint; it is a quality argument, not a
feasibility one.

**The clock does not drift; it starts late.** Measured against `performance.now()` with a source
running, in both loaders: the first two seconds after the context is created yield only ~1.31s of
`currentTime` — a one-time deficit of ~685 ms — and every settled second after that tracks wall
clock to within 0.1% (five consecutive intervals, ratios 0.996–1.009, 0.999 and 1.000 over the five
together). The deficit is the output device opening, and `outputLatency` reports 0, so nothing in
the API accounts for it. A native path must therefore take its time reference **after** the context
has settled rather than at creation; having done that, the clock is sound. This is not an origin
effect — both loaders measure the same.

`SharedArrayBuffer` stays `undefined` and `crossOriginIsolated` false in every case — a worklet here
is single-threaded, which rules out a threaded WASM build but not the worklet itself.

The drawing numbers are an *upper bound*, not a promise — see "what is hard" below.

## What a native path would skip, for the files that take it

None of this is removed — CDG + mp3 and mp4 still need every bit of it. It is skipped only for a
file the screen can open itself:

- `HlsMediaStreamService` — no ffmpeg spawn, no segmenting, no playlist, no session.
- The ffmpeg filter graphs for pitch, tempo and mix.

~~`PreparedMediaService` and the keyframe contract~~ — since removed outright, for everyone. The
per-song saving this section claimed (an 84-second render and 23.7 MB on disk per kit) has already
been taken; what a native path buys on top of it is playability, not speed.

## What it would have to reproduce

**The mix.** Today `LeadVolume` / `BackingVolume` reach ffmpeg's `amix` at stream-open time, so
changing either tears down the stream and rebuilds it behind a 600 ms debounce. Natively this is
three gain nodes. It stops being a rebuild and becomes a value change — this is the clearest
win after the render itself.

**Pitch and tempo.** Today: `asetrate` + `aresample` for pitch, `atempo` for tempo, `setpts` to
retime the picture, all fixed at stream open. Natively, tempo alone is easy (scale the clock,
`playbackRate` the sources) and pitch alone is easy (resample), but *independent* pitch and tempo
is a phase vocoder — and there is no AudioWorklet here to run one in. This is the hardest part
and the most likely reason to stop.

**Transport and sync.** This fits better than it does today. The host already anchors screens on a
clock: `SetTimelineCommand` carries a position and an `AnchorUtc`, and the screen computes expected
time from `Date.now() + clockOffsetMs`, where the offset comes from a 5-round-trip NTP-style
handshake re-run every 5 minutes. Today the screen's *actual* position comes from
`video.currentTime`, which is what forced seek-only correction (WKWebView walks `currentTime`
backwards). A live engine would read its own audio clock instead, which is the thing the sync
model wanted all along.

## Where this gets decided: the gate

`AGENTS.md` is explicit that `Render` is the moment that matters:

> **`Render` is the one that matters.** It is where licensed content leaves the provider's own
> container, so a refusal there means no playable copy is ever written; and it runs with nobody
> watching, so a gate refuses it outright where `Queue` and `Play` may raise a sign-in and carry
> on with the answer.

and:

> The gate is on the **source container**, not on the render.

Today the flow is: `PlaybackService.LoadAsync` asks `MediaAction.Play` before opening a stream, and
nothing asks `MediaAction.Render` at all — the one caller was `PreparedMediaService`. The enum
member survives for exactly this design.
The screen then receives only a URL — no file path, no stems, no decoder.

A native path moves that boundary. Handing stems to a screen process **is** content leaving the
container, and it happens with nobody watching, so it has `Render` semantics, not `Play` semantics
— the `Play` gate alone would be wrong, because `Play` may prompt for a sign-in and carry on while
`Render` must refuse outright.

This is an architectural decision, not an implementation detail, and it should be settled before
any code is written.

## What is genuinely hard

**Text fidelity.** The renderer shapes with HarfBuzz *per word* (not per syllable — that would
break joins), then recovers each syllable's glyph run by mapping HarfBuzz cluster indices back to
text offsets, and draws each syllable three times: black stroke outline, full inactive fill, then
a clip-rect active fill. Canvas2D has no positioned-glyph-run primitive. `fillText` cannot be told
"shape this exact run and let me re-clip it", and font fallback by codepoint
(`SKFontManager.MatchCharacter`) has no Canvas2D equivalent.

Pixel-identical output needs HarfBuzz in WASM plus `Path2D`. **The 10,500 fps above is `fillText`,
i.e. the easy version.** It is strong evidence there is headroom, not evidence the real renderer is
free.

**Cast — answered by the fallback.** A Chromecast receiver is handed the same HLS URL the screens
get, and native stems have no analogue. Since the render path is staying anyway, a kit that has to
cast simply renders as it does now. Worth noting the consequence: casting a kit keeps the 84-second
wait, so the two paths differ in more than fidelity.

**The command contract.** `ScreenCommandBase` is a closed `[JsonDerivedType]` set and
`LoadMediaCommand` carries a single URL. Carrying stems plus timing data means new command types,
which is a contract change. `ScreenCapabilities` has no notion of "can render natively", and it
needs one: with two paths coexisting, the host has to know which screens can take which, and an
older screen in the room must still get the stream. That negotiation is the real cost of a second
path, and it does not go away.

**Two paths, one transport.** Both paths have to answer the same `SetTimelineCommand`, report the
same state, and honour the same pause/seek/stop — including a room where one screen is native and
another is streaming the render of the same song. They must agree on position to well under the
150 ms realign threshold.

## Options

**A — Native path in Screen2.** For `.kit` only, the screen receives stems + timing and does
everything: decode, mix, draw. Falls back to the existing stream for Cast, for screens that cannot
render natively, and for every other format. Biggest win, and it owns pitch/tempo and text fidelity.

**B — Lyrics live, audio still ffmpeg.** The screen draws the lyrics over a locally-held
background, but audio still arrives as a host-mixed *audio-only* HLS stream. Keeps pitch, tempo,
mix and the gate boundary exactly where they are, and still removes the expensive half: no
per-frame rasterization, no x264, no 84-second render. Much the smaller change, and it sidesteps
both the AudioWorklet problem and the Vorbis question entirely. Costs: the timing data still leaves
the container, and a stream is still opened per song — so start-up is not instant, just cheaper.

**C — Keep rendering, make it cheaper.** Not a new engine at all. The backgrounds ship at
1920x1080 against a 1280x720 canvas, so every frame is downscaled and cropped on the CPU forever.
Pre-sizing them is a one-line change with no new risk.

These are not exclusive, and they are not really competitors: C is worth doing whatever happens,
and B is close to a strict subset of A — the drawing half of A with the audio half deferred. If A
is the destination, B is a sensible first step that ships value without committing to a phase
vocoder.

## B is the direction

Decided after the origin work above. Every cost the webview measurements uncovered belongs to A's
audio half, and B pays none of them: canvas and `fillText` are not secure-context gated, so the
page keeps arriving through `LoadRawString`; pitch, tempo and the mix stay in ffmpeg's filter
graph, so there is no phase vocoder to write; and the screen syncs its draw loop to the audio
element's `currentTime` rather than owning an `AudioContext`, so the device-open offset never
enters the picture. What was already built — the old engine's drawing half — is the half B keeps.

**A `.kit` is the plugin's, and stays the plugin's.** The host must not learn the container, so B
needs a format-agnostic contract in `KHost.Abstractions`: a plugin is asked for a *timeline* and a
set of *stems*, and answers for the file it owns. `KitContainer` and the timing parser already
exist in the KaraFun plugin's own repo; nothing of them moves here.

The shape:

- The plugin extracts its stems to files and parses its timing, and hands back both. The host
  learns nothing about Ogg, XML or `.kit`.
- The host mixes those stems through ffmpeg into an **audio-only** HLS stream — the same
  `LeadVolume` / `BackingVolume`, pitch and tempo that reach `amix` today, with no video encode
  and no rasterizer.
- The timeline goes to the screen as a command; the screen draws the words on a canvas over its
  background and follows the audio element's clock.

What that leaves untouched: CDG + mp3 and mp4 still stream exactly as they do now, the gate stays
where it is because the host still opens the media, and both paths still answer the same
`SetTimelineCommand` and agree on position inside the 150 ms realign threshold.

## What is built

Option B, end to end.

`lyrics-overlay.js` replaces `kit-engine.js`. It is the drawing half of the old engine and nothing
else: it takes the host's `TimedLyrics`, draws the chase on a canvas over whatever is playing, and
reads its clock from the element holding the song rather than owning one. The audio half — the
`AudioContext`, the stem decode, the gain mix and the video-element shape — is gone, along with
the `load-kit` and `kit-mix` browser messages. Option A's screen side is recoverable from git
history if the native path is ever revisited.

Host side, which was deliberately absent before:

- `TimedLyrics` and `ITimedLyricsProvider` in `KHost.Abstractions` — a plugin is asked for the
  words and answers for the file it owns, and nothing in the host parses a container.
- `TimedLyricsService`, the router, shaped after `MediaProbeService`: first provider to claim the
  path answers, one that throws deciding is skipped, one that fails on its own file ends the
  search. No fallback, because nothing generic can read timing out of an mp4.
- `SetTimedLyricsCommand`, sent from `LoadAsync` after the load command and before play. Sent even
  when there are none, or a screen keeps the last song's words over this one. A failure to read or
  send never fails the load.
- `BuildArguments` maps the picture optionally (`-map 0:v:0?`). A stems-only container has no video
  stream, and a required mapping onto one is fatal — ffmpeg exits before writing a segment, which
  reads as the song simply never starting.
- The page is told where its stream sits in the song (`songOffsetSeconds`, `rate`), because the
  words are written in song time and a stream opened at a seek starts at zero.

What it does not do yet:

- No artwork or background behind the words; the canvas is transparent over whatever is there.
- Text is laid out per syllable with `fillText`, not HarfBuzz glyph runs — see "what is hard".
- `ScreenCapabilities` is untouched: every screen is sent the words and draws them if it can. A
  `SupportsLyrics` flag is only worth adding when a screen exists that cannot.

## Open questions

1. Independent pitch and tempo. An AudioWorklet is available once the page has an origin, so the
   phase vocoder is a question of effort rather than of platform. The fallback answer is still
   there if it is not worth building: a kit whose pitch or tempo has been shifted plays from the
   stream, and only the unshifted case plays natively. Worth measuring how often a host actually
   shifts before spending a vocoder on it.
2. Does the clock hold? Mostly answered: the -700 ms was never drift but the ~685 ms the output
   device takes to open, and the rate after that is within 0.1% of wall clock (see "what the webview
   can do"). What is still untested is the same measurement with **real stems** decoding and mixing
   rather than one oscillator through a silent gain — the rate held under a trivial graph, which is
   not proof it holds under the real one.
3. What are the unidentified chunks (ids 5, 12-20) and the extra images?
4. What does WKWebView answer? Every number here is WebView2. Nothing on macOS has been tested at
   all — Vorbis decode, the canvas rate and the clock all need measuring there before this path is
   offered to a screen that is not on Windows.
5. Does the gate move to the hand-off, or does a native path only ever serve a screen in the same
   trust boundary as the host?
6. **How should the page get its origin?** Three ways were tried in this webview.
   - **`file://`** — measured end to end with the real player page: secure context, `AudioWorklet`
     present, hls.js and the overlay both inlined and live, the `ready` handshake and the state
     reports unchanged. It is a drop-in, and the cost is that the screen writes its page to disk
     where today it holds it in memory.
   - **`http://localhost` from the host's own server** — a real origin and same-origin with the
     media, at the cost of a screen that cannot draw anything until the host is serving.
   - **A custom scheme (`khost://`) through `RegisterCustomSchemeHandler`** — does not work, and
     cannot in Photino.NET 4.0.16 / Photino.Native 4.0.22. That handler is wired to WebView2's
     `WebResourceRequested`, which only sees sub-resource requests from a page already loaded;
     `Load()` reaches the native layer as a bare `ICoreWebView2::Navigate`, and Chromium rejects an
     unregistered scheme before any request is raised. The registration it would need
     (`ICoreWebView2EnvironmentOptions4::SetCustomSchemeRegistrations`, which is also where
     `TreatAsSecure` lives) is never called — the native layer creates the environment from the
     bare options class. Measured symptom: blank window, handler never invoked.

   One thing to check before moving: a secure context is also what turns a plain-`http` subresource
   into blocked mixed content. `http://localhost` is trustworthy and exempt, so a screen on the
   host machine is fine; a screen reaching the host at a LAN address over plain http has not been
   measured, and today's `about:blank` page is not subject to the rule at all.
