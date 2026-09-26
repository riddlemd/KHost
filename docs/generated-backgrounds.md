# Generated backgrounds for the render

Research note, branch `research/generated-backgrounds`. Findings and measurements only — nothing
here is implemented.

**Superseded in part.** The host-side render path this note builds on — `IMediaPreparer`,
`PreparedMediaService` and the stream-copy contract — has since been removed. Read the
measurements as a record of what was true then, not as a description of the code.

**The case.** A plugin that supplies audio-only media has no picture. CDG is out of scope: it draws
its own graphics, so a background behind it would be covered up anyway.

## What is there today

1. **Audio-only produces no video track at all.** `HlsMediaStreamService.BuildArguments`
   (`src/KHost.Domain/Services/HlsMediaStreamService.cs:258`) emits the `-c:v libx264` block
   unconditionally, but with no video stream on the input it produces nothing; the pre-render's
   `PreparedMediaService.BuildArguments` did the same before that path was removed. No `lavfi`,
   `color=`, `-loop 1` or `image2` input exists in the surviving builder. **There is no picture to
   preserve here** — adding a background is pure addition, not a downgrade of an existing copy path.
2. **One video-filter injection point, and it is `-vf`.** `BuildVideoFilter`
   (`HlsMediaStreamService.cs:502`) only ever emits `setpts=PTS/rate` for tempo. The comment beside
   it records that `-vf` was chosen *because* it composes with the CDG `-map`. A background needs a
   second input and `-filter_complex`, which is the one place this collides.
3. **`CopyPlan` already gates on keyframe cadence.** `CutsCleanly` (`HlsMediaStreamService.cs:393`)
   requires the render's keyframe interval to divide `Options.SegmentSeconds` (default 2, `:26`).
   This turns out to be the hinge — see below.
4. **A Chromecast only ever sees the muxed stream.** `CastService.ReceiverAppId` is Google's stock
   Default Media Receiver, `CC1AD845` (in the Chromecast plugin's own repo), loaded with a plain
   content URL. There is no receiver app of ours, so there is no page on which to draw an
   overlay.
5. **LocalScreen has no drawing surface.** No `<canvas>`, no WebGL, no `AudioContext` anywhere in
   `src/KHost.LocalScreen/screen-ui/`. Every overlay is DOM — marquee, QR codes, break-music card,
   next-singer card, still image — pushed as `IScreenCommand` JSON
   (`src/KHost.Abstractions/Services/IPC/ScreenCommands.cs`). `requestAnimationFrame` appears only
   in two volume-fade loops. There is no WebAudio tap on the HLS stream, so the browser has no
   frequency or amplitude data to react to.
6. **No plugin extension interface touches rendering.** The seven in
   `PluginLoader.ExtensionInterfaces` (`src/KHost.Domain/Services/Plugins/PluginLoader.cs:32`) are
   `IMediaProvider`, `IQueueRotationMode`, `IBreakMusicProvider`, `IPluginButtonHandler`,
   `IMediaPlaybackGate`, `IMediaPreparer`, `IMediaProbe`. None is visual.

## Where the picture has to be made

| | In the ffmpeg render | In the LocalScreen browser layer |
|---|---|---|
| Reaches Chromecast | yes | **no** — stock receiver, no page |
| Cost | an encode, unless copied | free |
| Audio-reactive | ffmpeg ships the filters | needs a WebAudio tap that does not exist |
| Existing machinery | `CopyPlan`, `CutsCleanly` | DOM overlays only |

**In the render.** Cast is a first-class output, and the whole point of this is media that has no
picture — a background the Chromecast cannot see leaves that output still blank.

## Pre-generated loops beat live generation

Measured on this machine (M-series, ffmpeg 9.0.1, `h264_videotoolbox`), 180 s of output, 1080p30,
audio from a real mp3:

| Approach | Speed | Video CPU |
|---|---|---|
| Generate the background live (`gradients` → encode) | **9.5×** realtime | full encode |
| Pre-generated 15 s loop, `-stream_loop -1`, `-c:v copy` | **150×** realtime | none — it is a remux |

Sixteen times faster, but the speed is the smaller half of the argument. **The loop turns the
background into a stream copy**, which is the path the codebase already has machinery for. Author
the loop with `-force_key_frames "expr:gte(t,n_forced*2)"` and its keyframes land at exactly
0, 2, 4 … 14 s (verified with `ffprobe -skip_frame nokey`), so `CutsCleanly` is satisfied by
construction rather than by luck.

A 15 s 1080p loop at 5 Mbit is **2.0 MB**. A dozen of them is under 30 MB.

## The seam is real, and it is solved once

A generator run for 15 s does not return to where it started, so a naive loop cuts hard every
15 seconds. Measured as PSNR between the last frame and the first:

| | PSNR |
|---|---|
| Naive 15 s loop, wrap | **20.2 dB** — a visible cut |
| Crossfade-wrapped loop, wrap | **53.1 dB** |
| Two *adjacent* frames mid-loop (the invisible baseline) | 48.8 dB |

The wrapped loop's seam is a **smaller change than one ordinary frame step**. The recipe: generate
20 s, then blend the trailing 5 s back over the opening 5 s and concatenate the remainder —

```
[0:v]split=2[a][b];
[a]trim=0:15,setpts=PTS-STARTPTS[head];
[b]trim=15:20,setpts=PTS-STARTPTS[tail];
[head]split=2[h1][h2];
[h1]trim=0:5,setpts=PTS-STARTPTS[h_in];
[h2]trim=5:15,setpts=PTS-STARTPTS[h_rest];
[tail][h_in]blend=all_expr='A*(1-(T/5))+B*(T/5)'[mixed];
[mixed][h_rest]concat=n=2:v=1:a=0[v]
```

This is a one-off authoring cost, paid when the loop is made, never at play time.

## What ffmpeg gives for free

- **Generators**: `gradients`, `perlin`, `cellauto`, `life`, `mandelbrot`, `sierpinski`,
  `colorspectrum`, `zoneplate`, `testsrc2`.
- **Compositing**: `overlay`, `blend`, `xfade`, `displace`, `zoompan`, `hue`, `gblur`, `geq`,
  `scroll`, `tblend`, `feedback`.
- **Audio-reactive (A→V)**: `showcqt`, `showcwt`, `showspectrum`, `showwaves`, `showvolume`,
  `avectorscope`, `a3dscope`.
- **Portability trap**: `coreimagesrc` and `coreimage` are the most capable generators on this Mac
  and **do not exist in a Windows ffmpeg build**. Anything built on them is macOS-only. Likewise
  `h264_videotoolbox` — Windows needs `libx264` or an NVENC/QSV path.
- `perlin` takes no duration option; bound it with `-t`, not `d=`.

## The trade the loops force

**Audio-reactive and stream-copy are mutually exclusive.** A loop is fixed and cannot answer the
song. Two tiers fall out:

- **Tier 1 — looped decorative background.** Stream copy, effectively free, reaches Cast.
- **Tier 2 — audio-reactive.** Requires the composite encode: measured **8.9× realtime** for
  `gradients` + `showcqt` overlaid at 1080p30 with hardware encoding, and **4.8×** for a
  `perlin` + `hue` + `gblur` chain. Affordable, but it competes with the single render slot in
  `PreparedMediaService` and it is a full encode on every song.

Tier 1 is the one to build first; tier 2 is the same plumbing with the copy turned off.

## How it would attach

- **Backgrounds as files, not filter graphs.** This is the simplification that makes the plugin
  angle cheap: a plugin registers a background by pointing at a media file, so no filter-graph API
  has to be designed, versioned, or sandboxed. It also means a venue can drop in its own clip.
- **The QR code is the precedent to copy.** A standing registration in the manifest read without
  resolving the plugin, plus a live `IPluginContext.RegisterQrCodeAsync`; the venue names **one**
  source in `Venue.Settings`, none by default, and every other owner's is held rather than drawn.
  A background source wants exactly that shape.
- **Gate the new path on "the source has no video stream."** That is precisely when the CDG `-map`
  is not in play, so the `-vf` → `-filter_complex` collision at `HlsMediaStreamService.cs:502`
  never arises. CDG keeps its own picture and is untouched.

## Open questions

- **Staleness.** One loop all night is noticeable. Rotate a set per song, or tint per song with
  `hue` — but a tint is a filter, and a filter costs the stream copy. A per-song *choice* from a
  pre-rendered set is free; a per-song *tint* is not.
- **Cadence matrix.** A loop must match `Options.SegmentSeconds`. If that stays configurable, either
  loops are regenerated when it changes or a set is kept per cadence.
- **Resolution.** A 720p loop scaled to 1080p costs the copy. Loops probably need to be authored at
  the output resolution, which multiplies the matrix again.
- **Who authors the loops** — shipped with the host, generated on first run (about 2 s each at the
  measured rates), or supplied by the plugin.
- Whether tier 2 should exist at all before anyone asks for it.

---

# Proof of concept

Four loops built with `docs/assets/generated-backgrounds/mkloop.sh` — 15 s, 1920×1080, 30 fps,
`h264_videotoolbox` at 5 Mbit, keyframes forced every 2 s to match `Options.SegmentSeconds`.
`ffprobe -skip_frame nokey` confirms keyframes at exactly 0, 2, 4 … 14 s on all four, so
`CutsCleanly` holds.

| loop | source | size | look |
|---|---|---|---|
| `aurora` | `gradients` ×4 deep colours, `gblur=40`, vignette | 3.0 MB | near-black with a soft blue/magenta glow. The safest. |
| `nebula` | `perlin` (5 octaves, `xscale=0.7`), colour-ramped, vignette | 3.0 MB | purple cloud structure |
| `plasma` | `geq` sin field, `clip()`ed, scaled up, vignette | 3.0 MB | slow red/blue wash |
| `embers` | `perlin` thresholded, screen-blended glow copy | 3.0 MB | large warm motes — the only non-blue option |

**Two generator traps, both hit on the first attempt.** `geq` channel expressions **wrap** rather than
clamp, so `48+40*sin(..)+28*sin(..)` went negative and came back as bright green bars — every channel
needs `clip(expr,0,255)`. And `perlin`'s `xscale`/`yscale` are inverted from the intuition: a *higher*
value means faster variation and therefore *smaller* features, so a nebula wants `xscale≈0.7`, not 2.5.

## The seam held through encoding

Measured on the finished MP4s, wrap (last frame vs first) against the adjacent-frame baseline in the
same file — the baseline is what an ordinary frame step costs at this bitrate, so it is the bar:

| loop | wrap | adjacent-frame baseline |
|---|---|---|
| `aurora` | 46.5 dB | 51.3 dB |
| `nebula` | 47.2 dB | 48.7 dB |
| `plasma` | 46.7 dB | 50.9 dB |
| `embers` | 46.6 dB | 44.5 dB |

All four land at 46–47 dB against a naive loop's **20.2 dB**. The wrap is within a few dB of an
ordinary frame step, and on `embers` it is smaller than one. Seamless in practice.

## Lyric legibility is measured, not eyeballed

Luminance of the lyric band (lower third, y 780–1000) across all 450 frames, Y on the 16–235 scale.
White text with a black outline needs this band to stay low:

| loop | YAVG median | YMAX median | **YMAX worst frame** |
|---|---|---|---|
| `aurora` | 23 | 39 | 40 |
| `nebula` | 20 | 32 | 71 |
| `plasma` | 23 | 35 | 45 |
| `embers` | 23 | 63 | **139** |
| `embers` + scrim | 19 | 36 | **88** |

A single frame looks fine on all four; the loop-wide worst case is what separates them. `embers`
drifts a bright mote through the lyric band and peaks at 139, which squeezes the *sung* line
(yellow) even with an outline.

**The rule this gives: composite a bottom scrim into every loop, at authoring time.**
`docs/assets/generated-backgrounds/scrim.svg` — transparent to 55% height, ramping to 80% black at
the bottom — takes `embers` from 139 to 88 while moving the band average only 23 → 19, so the
picture above the lyrics is untouched. Applying it per loop costs nothing at play time and removes
the need to vet each generator by hand.

## ffmpeg here cannot draw text

The Homebrew build has **no `drawtext`, no `subtitles`, no `ass`** — it is configured without
`libfreetype` and `libass`. Consequences, in order of importance:

1. **It reinforces the loop plan.** Nothing can be burned in, so the video track stays
   a pure background and stays stream-copyable.
2. **Lyrics have to be composited some other way.** Rendering the lyric page to a transparent PNG and
   using `overlay` works and needs no ffmpeg text support at all — that is how the legibility images
   above were made (`rsvg-convert` → `overlay`). A syllable wipe would be an alpha mask animated over
   that PNG rather than 30 re-renders a second.
3. **This constrains host-side text only.** It does not stop a plugin: a plugin's own preparer
   paints its own lyric frames and hands back a finished picture, so it never asks ffmpeg to set
   type. And because those lyrics are *in the picture*, a Chromecast does show them — the stock
   receiver needs no overlay when the text is already muxed in.

Requiring an ffmpeg built with `libfreetype`/`libass` is the other way out, but it makes the host's
ffmpeg dependency stricter than "whatever is on the machine".

# How a stems-and-lyrics format interacts

A stems-and-lyrics format shipped by one plugin is a multi-stream container carrying timing XML,
Ogg Vorbis **stems** (instrumental, backing, lead — a duet ships two named leads), **JPEG background
art**, PNG overlay art, and audio-reactive visualiser presets. Its picture is built in layers:
background art, then a lyric layer with per-syllable wipe timing between an active and inactive
colour, then an effects layer that is either a visualiser preset or timed decorative text. Stems are
mixed live; no mixdown is baked in.

Four consequences, and they do not all point the same way.

- **The picture is a still, not a video — the format has no video stream type at all.** So every
  song in this format is by definition media with no picture track, which is exactly the case this
  note is about.
- **But the format ships its own background art, so the picture is not actually missing.** A
  generated loop is an *upgrade* (motion instead of a still) or a fallback for a file with no art —
  and none without art has been observed. This weakens the premise that this format is the consumer
  that needs generated backgrounds most.
- **Stems make this format the ideal shape for copying the picture.** Multi-stem audio is always
  rebuilt, which is already what `CopyPlan` expects: `Picture = !Whole && CanCopyVideo(tempo)`. A
  looped background copies while the stems mix. Nothing new is needed for that to work.
- **The format's own answer for motion is its visualiser presets, not a generic loop.** A file names
  the visualiser it wants. A generic loop is less faithful than what it asks for; matching it means
  an audio-reactive preset engine, which is a far larger job than any of this.

**Lyrics are already solved, by the plugin, inside its own rendition.** The plugin implements
`IMediaPreparer` and burns the syllable-timed lyrics into the picture it produces — which is exactly
what the contract was written for: *"One may be stems and a timing document, not a video, so nothing
downstream can open it: the plugin renders it and the host plays what comes out"*
(`src/KHost.Abstractions/Services/IMediaPreparer.cs:4-7`). The host's own `ILyricsService` is
unrelated — it is an LrcLib lookup feeding the console's `ShowLyricsDialog`, and never reaches a
screen.

That changes where a background would attach. **It is the plugin's picture, not the host's**, so a
generated loop has to be composited inside `PrepareAsync`, under the lyric layer, before the render
is handed back. The host never sees a separable background to apply.

The rest of the pipeline is already the right shape for it, and needs nothing new:

- The render carries the **stems as separate audio streams on the one file**, and the host re-mixes
  them at stream time — `BuildMixGraph` addresses them as `[0:a:{track.Index}]`, roled Music / Lead /
  Backing (`src/KHost.Domain/Services/HlsMediaStreamService.cs:460-498`).
- So the picture copies while the audio rebuilds, which is precisely
  `Picture = !Whole && CanCopyVideo(tempo)` in `CopyPlan` (`HlsMediaStreamService.cs:378-385`).
- The plugin declares its own cadence through `KeyframeSeconds`, relayed by
  the pre-render into `CutsCleanly` before that path was removed.

**The consequence for the loop economics.** The plugin is encoding its picture either way, so the
150× stream-copy win does **not** apply to this format — that win lives at the host's copy path, which is
already being taken on the finished render. Inside `PrepareAsync` a pre-generated loop saves only the
*generation* cost, not the encode: the measured gap there is the 9.5× live-generate figure against a
cheaper decode-and-composite, not 150×. **The large win stays with providers that ship audio and
nothing else.**

Also note `PrepareAsync(filePath, destination, ct)` passes **no resolution and no segment length** —
the plugin picks both. A loop library shipped by the *host* could not be matched to the plugin's
chosen output automatically, which is a further argument for the loops being the plugin's own asset
rather than a host feature.

## Revised ordering

1. **Ship the loop library and the scrim rule.** Cheap, self-contained, and it serves every provider
   that hands over audio and nothing else — which is where the 150× copy win actually lands.
2. **Decide whose asset the loops are.** For this format they have to be composited inside
   `PrepareAsync`, at a resolution the plugin alone knows, so they are more naturally the plugin's
   own asset than a host feature. A shared *authoring* recipe (`mkloop.sh` + the scrim) is the part
   worth having in common.
3. **Treat a generated loop for this format as an aesthetic option, not a gap being filled.** Files
   in this format ship their own background art, and its own answer for motion is its visualiser
   presets. Leave those alone until someone asks.

---

# Where it attaches in the plugin

Read from the plugin's own repo. Its media provider implements `IMediaProvider`,
`IPluginButtonHandler`, `IMediaPlaybackGate`, `IMediaPreparer` and `IMediaProbe`; `PrepareAsync`
hands off to its own Skia-based renderer.

**The rendition is Skia painting into an ffmpeg pipe.** SkiaSharp paints every frame, snapshots it
to a pixel buffer and writes it to ffmpeg's stdin as raw RGBA — `-f rawvideo -pix_fmt rgba -s WxH
-r 30 -i pipe:0`. ffmpeg only demuxes the stems and encodes. Its own comment is explicit that this
is to skip `libass`/`drawtext` entirely — so **the missing `drawtext` in the host's ffmpeg does not
touch this plugin at all**, and the lyrics being *in the picture* is why a Chromecast shows them.

Facts that constrain a background:

- **Output is 720 tall, and the width is derived per file** from the lyric layout's bounding box —
  so there is no fixed aspect ratio to author against.
- **`libx264 -preset veryfast`, 30 fps**, not hardware encoding.
- **`KeyframeSeconds` is 2**, forced with `-force_key_frames expr:gte(t,n_forced*2)
  -sc_threshold 0` — already matching the host's default `SegmentSeconds`, so `CutsCleanly` holds
  and the picture copies.
- **Stems stay separate**: one AAC track per Ogg stem, instrumental default.

**There is already a background step to replace.** Every frame begins
`canvas.Clear(new SKColor(8, 8, 12))`; if the file carries cover art it is painted to cover, dimmed
to alpha 150, then covered by a black rect at alpha 90. That dim-and-scrim is the plugin
independently arriving at the same conclusion the luminance measurements above reach — **treat it
as the precedent, and give a loop the same treatment.**

## The change: pipe alpha, let ffmpeg overlay

Compositing a *video* loop in Skia would mean decoding video inside .NET, which SkiaSharp cannot do.
The way round it is to leave the loop in ffmpeg, where a decoder already is:

1. Skia keeps painting only the lyric layer, but on a **transparent** canvas instead of `Clear(8,8,12)`.
2. The background becomes a second ffmpeg input, `-stream_loop -1 -i <loop>.mp4`.
3. `-map 0:v` becomes a `filter_complex` that scales-to-cover, crops to the file's canvas, and
   overlays the piped layer.

Verified end to end at the plugin's own settings (1280×720, 30 fps, `libx264 veryfast`, keyframes
forced every 2 s), rendering 60 s of output:

```
-stream_loop -1 -i aurora.mp4  -f rawvideo -pix_fmt rgba -s 1280x720 -r 30 -i pipe:0
-filter_complex "[0:v]scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720[bg];
                 [bg][1:v]overlay=0:0:shortest=1[v]"  -map "[v]"
```

Keyframes land on 0, 2, 4 … and the picture is correct. **No video decoding enters .NET.**

| | speed | output |
|---|---|---|
| today — opaque frames on `SKColor(8,8,12)`, encode only | 28.7× realtime | 96 KB / 60 s |
| proposed — alpha layer overlaid on a looped background | **9.0× realtime** | 4.0 MB / 60 s |

**Be honest about where that cost comes from: it is not the overlay, it is the encoder.** A flat
near-black field with text on it compresses to almost nothing; a moving background is real content.
A four-minute song goes from roughly 8 s of render to roughly 27 s — both comfortably inside the
pre-render budget, and both far from the single render slot being the bottleneck, but it is a 3×
increase and a ~40× larger file.

**One trap, hit while testing this.** `overlay` takes its **alpha from the base**, so compositing the
lyric layer onto a transparent scratch surface before handing it over yields a fully transparent
result and the lyrics silently vanish — the encode still succeeds and the background still looks
right. Pipe the lyric layer as the *overlay*, never as the base.

---

# Hardware encoding, re-examined

Re-measured once backgrounds were implemented, because the composite tripled the encode cost and
made the encoder look like the thing to fix. **It is not.** Everything below is 120 s of output at
1224×720 (a real canvas size from this format), on this Mac.

**Encoder in isolation**, same decoded source, so the only variable is the encoder:

| config | speed | size | keyframes | SSIM | PSNR |
|---|---|---|---|---|---|
| `libx264 -preset veryfast` | **30.3×** | 7.5 MB | 0, 2, 4 … | 0.99795 | 55.5 dB |
| `h264_videotoolbox -b:v 6M` | 19.6× | 19 MB | 0, 2, 4 … | 0.99842 | 57.2 dB |
| `h264_videotoolbox -q:v 55` | 19.8× | 2.0 MB | 0, 2, 4 … | 0.99715 | 51.5 dB |

**libx264 `veryfast` wins on every axis that matters here.** It is 55% faster than the hardware
encoder, and at a comparable quality videotoolbox needs roughly 2.5× the bits (19 MB against 7.5 MB
for +1.7 dB); dropped to a smaller file it is 4 dB *worse* than x264 while still being slower. The
content is the reason — a slow gradient under static text is exactly what a software encoder's
motion search disposes of cheaply, and the hardware block's per-frame overhead dominates at 720p.

**`h264_videotoolbox` ignores `-force_key_frames`.** It produced keyframes every 0.4 s regardless,
which is what inflated the 6 Mbit run to 19 MB; `-g 60 -keyint_min 60` is what actually controls it.
That is a trap worth recording even though the plugin is not switching: `KeyframeSeconds` is a
promise the host checks `CutsCleanly` against, and an encoder swap that silently ignores the
cadence argument would keep compiling and keep rendering while quietly making that promise false.
`-g` is also frame-counted, so it matches one frame rate only — the reason the expression form was
chosen in the first place.

**The encoder was never the bottleneck.** The full pipeline — pipe, overlay, encode — runs at 17×
while the encoder alone runs at 30× and the source alone at 155×. What the background actually costs
is the compositing, and there the measurable win is elsewhere:

| full pipeline, 120 s | speed |
|---|---|
| loop rescaled every frame (as implemented) | 17× |
| loop pre-scaled to the canvas | **20.5×** |

Pre-scaling the loop to the file's canvas is worth about **21%**, and it is the only optimisation here
with a real number behind it. It is deliberately not built: the canvas size is derived per file, so a
cache would be keyed on a dimension pair and would need invalidating against the loop file — real
complexity for a render that already runs many times faster than realtime and sits off the path
anyone waits on. Worth doing if the render ever becomes the thing holding a song up; not before.

**Verdict: keep `libx264 -preset veryfast`.** Hardware encoding is slower, bigger for the quality,
and loses the cadence control the host's copy path depends on.
