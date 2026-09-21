# Playing a `.kit` natively

Why `kit-engine.js` exists, and what is still unanswered about it.

The question: instead of rendering a `.kit` to mp4 with SkiaSharp and ffmpeg and then streaming
that back as HLS, could a screen decode the stems and draw the lyrics itself?

The numbers below came from a throwaway `KHost.Screen3` probe, which has served its purpose and
been deleted. Its measurements are kept here because the engine's design rests on them.

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
| **AudioWorklet** | **no** |

Two of these matter more than the rest.

**Vorbis decodes.** This was the risk that could have ended the idea: `canPlayType` answers
"probably" for codecs that then fail on real data, and WebView2 is not Chrome. It decoded an
actual stem. The stems can be handed to the screen as-is.

**There is no AudioWorklet.** That removes the obvious way to build a phase vocoder, which is
what independent pitch and tempo shifting needs. See the open questions.

The drawing numbers are an *upper bound*, not a promise — see "what is hard" below.

## What a native path would skip, for the files that take it

None of this is removed — CDG + mp3 and mp4 still need every bit of it. It is skipped only for a
file the screen can open itself:

- `HlsMediaStreamService` — no ffmpeg spawn, no segmenting, no playlist, no session.
- `PreparedMediaService` — its whole purpose is to make starting a song a stream copy, and there
  is no stream to copy. The render stays available as a fallback (see Cast, below).
- The keyframe contract with it: `CopyPlan`, `CutsCleanly`, `CanCopyVideo`, `CanCopyAudio`,
  `KeyframeSecondsFor`, the segment-length setting. All of it exists so a muxer can cut an
  already-encoded file without re-touching frames.
- The ffmpeg filter graphs for pitch, tempo and mix.

The saving is per-song, not architectural: an 84-second render and 23.7 MB on disk, gone, for
each kit a venue plays. The machinery itself stays exactly where it is.

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

Today the flow is: `PreparedMediaService` asks `MediaAction.Render` immediately before writing any
bytes, and `PlaybackService.LoadAsync` separately asks `MediaAction.Play` before opening a stream.
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

## What is built

`kit-engine.js` — the screen side of option A, and nothing else. It decodes stems through Web
Audio, draws the lyrics on a canvas, and wears the shape of the video element it stands in for
(`currentTime`, `duration`, `paused`, `readyState`, `volume`, `play`/`pause`) so `player.js` can
route the transport, the correction loop and the state report to whichever is holding the song.

Deliberately **not** built: the host side. `ScreenCommands`, `ScreenCapabilities`,
`PlaybackService` and the Abstractions contract are untouched, so nothing in the app selects this
path yet. It answers a `load-kit` browser message and is otherwise inert.

What it does not do yet:

- No pitch or tempo. See question 1 below.
- No artwork or background behind the words; the canvas is transparent over whatever is there.
- Text is laid out per syllable with `fillText`, not HarfBuzz glyph runs — see "what is hard".

## Open questions

1. Can independent pitch and tempo be done without an AudioWorklet? With the render path staying,
   there is a third answer available besides "solve it" and "lose it": a kit whose pitch or tempo
   has been shifted falls back to the stream, and only the unshifted case plays natively. Whether
   that is acceptable depends on how often a host actually shifts — worth measuring before
   designing around it.
2. Does the clock hold? The probe measured `AudioContext.currentTime` drifting to about -700 ms and
   then flattening — but it measured with **nothing playing**, which is not the real case. In Chrome,
   with silent sources actually running, the offset was a flat -40 ms matching the reported
   `outputLatency`. Re-measure with stems playing before trusting either number.
3. What are the unidentified chunks (ids 5, 12-20) and the extra images?
4. What does WKWebView answer? Every number here is WebView2. Nothing on macOS has been tested at
   all — Vorbis decode, the canvas rate and the clock all need measuring there before this path is
   offered to a screen that is not on Windows.
5. Does the gate move to the hand-off, or does a native path only ever serve a screen in the same
   trust boundary as the host?
