# What an `IMediaRenderer` is

**Implemented, except where noted below.** Stages 1-3 are built; stage 4 is deliberately not started — see **Staging**. This settles the shape and its reasoning first, the same way
`docs/display-provider.md` did, because the last thing built in this area was built and then backed
out when it was measured.

> A media renderer owns **turning one file into something a display can actually play**. It is asked
> once, when a song starts, and it answers with what to play: a stream the host encodes, a file the
> display reads directly, or the separate parts a display mixes for itself. It does not decide *what*
> the show is, or where it comes out — it is told.

## Why it exists

The decision "how does this file become sound and picture" is currently made in four places, none of
which owns it:

- the plugin, in `IPlayableMediaSource.ResolvePlayableAsync` and `StemsOf` — turns a container into
  parts
- `HlsMediaStreamService.OpenAsync` — resolves, then **always** encodes
- `PlaybackService.DescribeStems` — gates on pitch, tempo, and whether stems agree with the tracks
- `screen-ui/player.js` — picks stem mixing over the stream

That spread has a measurable cost right now. A kit played on a screen that mixes takes the stem
branch in the page and never constructs hls.js, so the playlist is never fetched — while the host
runs a full libx264 + AAC encode of the whole song for a consumer that does not exist. Nothing in
the pipeline can say "this one needs no encode", so every format gets the same treatment.

## The three cases, side by side

Three formats were surveyed specifically because they disagree on every axis that matters.

| | **CDG + MP3** | **MP4** | **`.kit`** |
|---|---|---|---|
| Files in | two — graphics plus a sibling `.mp3` | one | one container, demuxed to N stems |
| Picture | **synthesized** — ffmpeg decodes the subcode graphics | already encoded in the file | **none at all** (see below) |
| Audio | the companion `.mp3`, ffmpeg input 1 | embedded in the container | N Ogg Vorbis stems |
| Words | burned into the picture | burned in, or none | `ITimedLyricsProvider` |
| Needs a host encode? | **always** | no, unless shifted | no, unless shifted |
| Seeking | must seek on **output** | input seek is fine | free — the parts are the whole song |
| Levels rideable at play? | no | no | yes |
| What is cleaned up | the session directory | nothing — a library file | the session directory |

Where each of those lives today:

- **`-r 30` for graphics only** — `HlsMediaStreamService.cs:270-271`, keyed off `IsGraphicsOnly`
  (`:316-317`). A `.cdg` emits a frame only when the graphics change, so without it the segment
  durations drift off the wall clock.
- **The companion `.mp3`** — `ResolveCompanionAudio` (`:321-327`) is `Path.ChangeExtension(path,
  ".mp3")`, existence-checked, and becomes input 1 with an explicit `-map 0:v:0 -map 1:a:0`
  (`:255-260`). Missing, the stream is silent and only a warning says so (`:97-99`).
- **Output seek** — was `seekOnOutput = companionAudioPath is not null` (`:248`). A CDG decode is
  stateful, so seeking the input corrupts it. A genuinely format-specific rule sitting in a
  format-agnostic service, reached through a variable that named the *companion audio* rather than
  the thing it was about — and wrong for a `.cdg` with no `.mp3` beside it. Now keyed off
  `IsGraphicsOnly`; see staging step 3.
- **`-map 0:v:0? -map "[a]"`** (`:291`) — the `?` exists so a mix graph tolerates a source with no
  video, which is the kit case leaking into the general path.
- **The kit's remux** — `BuildRemuxArguments` (`KaraFunMediaProvider.cs:1849-1869`) stream-copies
  the stems into a Matroska `.kfa` with **no video stream**, purely so `HlsMediaStreamService` has
  something to open.

### A kit has no picture, and that is not an oversight

`RenderAsync`/`KitRenderer` in the plugin — the SkiaSharp path that used to pick a backdrop — is dead
code, and its own comment says so (`KaraFunMediaProvider.cs:1494-1499`). Nothing in the host calls
it. So a kit plays as a black screen with the words drawn over it.

This matters to the shape: the renderer for a kit is the one that would eventually have to answer
"and what goes behind the words" — a still, a video bed, or a generated visualisation. Any of those
is a rendition, not a special case bolted onto playback.

## What the differences demand of the interface

Lining the three up produces five things the abstraction has to express, and today's pipeline can
express none of them.

1. **A rendition is not always a stream.** It may be a URL to encode into, a URL to a whole file, or
   a set of parts with no single URL at all. `LoadMediaCommand.StreamUrl` being `required` is the
   current shape's way of insisting otherwise.
2. **Whether a seek needs a rebuild.** An HLS stream is cut at the playhead, so its zero is
   `StreamStartOffset` and seeking outside the transcoded range means reopening. A whole MP4 or a set
   of stems is the entire song and a seek is free. That distinction is implicit in `StreamStartOffset`
   today; with three renderers it has to be stated.
3. **What the target can do.** Whether an encode is needed depends on the display, not only the file:
   the same kit needs one for a Chromecast and none for a screen. So the request carries the target's
   capabilities, and `DisplayDevice.SupportsStemMix` stops being something `PlaybackService` reads on
   the side.
4. **Some rules are about the decode, not the format.** Output-seek belongs to "stateful graphics
   decode", not to "there is a companion mp3". Moving it into the renderer that owns CDG lets it be
   named for what it is.
5. **Not every rendition has a session.** A directly-played library file writes nothing and needs no
   temp directory; the session is currently both the cleanup unit *and* the URL namespace.

## The shape

```csharp
public interface IMediaRenderer
{
    /// <summary>Whether this renderer owns that file.</summary>
    bool CanRender(string filePath);

    Task<MediaRendition?> RenderAsync(MediaRenderRequest request, CancellationToken cancellationToken = default);
}
```

**`CanRender` is asked once per song, at play — not per queued turn.** This is the one place it
differs from `IMediaPlaybackGate.Claims` and `IMediaProbe.CanProbe`, which answer from the path alone
because they run for every queued turn on every reconcile. A renderer may therefore open the file,
which is what lets a direct-play renderer check the codecs before claiming an MP4. Say so in its
docs, or someone will copy the "path alone" rule across and lose the only reason it can work.

```csharp
public sealed class MediaRenderRequest
{
    public required string FilePath { get; init; }
    public TimeSpan StartOffset { get; init; }
    public int Pitch { get; init; }
    public int Tempo { get; init; }
    public AudioMix? Mix { get; init; }
    public required RenderTarget Target { get; init; }       // what the display can do
}

public sealed class MediaRendition
{
    /// <summary>One thing the display plays end to end, when there is one.</summary>
    public string? Url { get; init; }

    /// <summary>Parts the display mixes itself; empty for everything that is not a kit today.</summary>
    public IReadOnlyList<StemSource> Stems { get; init; } = [];

    /// <summary>Where this rendition's zero sits in the song.</summary>
    public TimeSpan StartOffset { get; init; }

    /// <summary>False for a stream cut at the playhead, true for a whole file or a set of parts.</summary>
    public bool SeekableInPlace { get; init; }
}
```

A renderer that needs somewhere to write asks for it rather than being handed it: as built,
`IMediaStreamService.OpenWithoutEncodeAsync` returns a served, swept session directory with no
ffmpeg behind it, and `BuildArtifactUrl` names the URL a display fetches from it. That keeps the
route's shape in one place and answers open item 5 — a rendition without an encode still has a
session, it just has no process in it.

Returning **null** means "nothing to do here" and falls through to the next renderer, the same
contract `ResolvePlayableAsync` already uses. That is how a kit on a Chromecast works: the kit
renderer writes its stems, sees a target that cannot mix, remuxes, and hands the `.kfa` on to the
streaming renderer rather than trying to encode itself.

### The renderers

| Renderer | Claims | Answers |
|---|---|---|
| `StreamingMediaRenderer` (fallback, keyed) | everything | the HLS session it builds today; owns the CDG rules |
| `DirectMediaRenderer` | containers the target plays as-is, unshifted | a URL to the library file, `SeekableInPlace` |
| the kit renderer (plugin) | `.kit` | stems for a mixing target; otherwise a `.kfa` to encode |

The fallback is registered **keyed**, exactly as `FfprobeMediaProbe` is behind
`MediaProbeService.FallbackKey`, so it never appears in the `IMediaRenderer` enumerable — it claims
every file and would win every race.

### Why not key off the extension

It is close, and it is nearly enough. A renderer declares `[".kit"]`, the host builds a dictionary,
and what claims what is readable without running anything. For the formats here it would work: a
played `.cdg` pair is always the `.cdg`, because the graphics half is **the half that becomes the
library row** and the audio beside it is found at play time, never imported separately
(`KHost.Common/Media/MediaFormats.cs:36-40`). The `.mp3`-with-a-`.cdg`-beside-it branch in
`IsKaraokeTrack` is there so the *scanner* does not mint a second row for the audio half; every
caller of it is an import or browse path, not playback.

`CanRender(path)` is still the better key, for one reason that survives and one that is about later.

- **Whether an MP4 can be played directly is a question about its codecs**, not its name. H.264 and
  AAC yes, HEVC or DTS no. An extension key would have the direct renderer claim files it then
  cannot render and hand back a null — which is `CanRender` again, only later and less honestly.
  Declaring extensions forecloses looking; a predicate does not.
- **It costs a plugin nothing either way.** `bool CanRender(string p) => IsAKit(p);` is the same line
  as declaring `[".kit"]`, so the flexibility is free. Most renderers really will be that one line.

The usual argument for a dictionary does not apply: `CanRender` is asked **once per song**, not per
queued turn like `Claims` and `CanProbe`, so iterating every renderer costs nothing.

Collisions are answered the way `IMediaProbe` answers them — first claim wins, and the greedy
fallback is registered keyed so it never enters the race. An extension dictionary would not have
removed that problem, only renamed it to "two plugins both declared `.mp4`", resolved by insertion
order with nothing to read.

### Why not key off `MediaType`

Because the type does not carry the distinction. `MediaFormats.TypeForFile`
(`KHost.Common/Media/MediaFormats.cs:58-74`) has no idea `.kit` or `.kfa` exist — KaraFun claims
those extensions independently — and a plain `.mp4` comes back `Karaoke` or `Video` depending on a
flag the *caller* passes, not on anything about the file. An enum switch would therefore have to be
extended by the host every time a plugin brought a format, which is the opposite of the point.
Claim-by-file is the pattern this codebase already uses for exactly this reason.

## What this replaces

`IPlayableMediaSource` folds into it. "Turn this into something playable" and "decide what playing it
means" are the same question asked twice, and splitting them is why the encode decision ended up in a
service that only knows how to encode. `StemsOf`, added days ago, is the seam showing.

`HlsMediaStreamService` survives, wrapped — the same way `IScreenServer` survived behind
`ScreenDisplayProvider`. Its ffmpeg argument building is not the problem; being the only answer is.

## Risks

- **This is not the deleted render path, and it will be mistaken for it.** `AGENTS.md` says flatly
  "There is no host-side render path" and records why `IMediaPreparer` and `PreparedMediaService`
  went: they produced a cached artifact ahead of time and grew eviction, progress reporting and state
  that outlived the song, for ~0.1–0.2s of start latency and no reliable CPU saving. This is a
  per-play router that answers "how does this file reach this display", produces nothing that
  outlives the session, and has no cache. **If that distinction is not written into `AGENTS.md` at
  the same time, someone deletes this in six months and cites that line correctly.**
- ~~**The name collides.**~~ Avoided: the plugin implements `IMediaRenderer` on
  `KaraFunMediaProvider` itself — one singleton across every extension interface, as the plugin
  rules require — so no new type sits beside the dead SkiaSharp `KitRenderer`.
- **Direct play needs a new endpoint, and it is the risky one.** Nothing today serves a library path
  with ranged GETs: `MediaStreamEndpoints` deliberately restricts to bare filenames inside an active
  session's temp directory. A route that serves library files must be keyed by media id with a
  session-scoped token, never by path, or an unauthenticated LAN-facing surface can read the library.
  `MediaImageEndpoints` is not a precedent to copy — it serves stills without
  `enableRangeProcessing`, so it cannot carry playback anyway.
- **Windows is mid-test on the current shape.** `khost-desktop` is smoke testing the stem mixer as it
  stands. Its result decides whether the stem path is viable at all on the weaker box, and this
  refactor should not land before that answer arrives.

## Open questions

1. Does `DirectMediaRenderer` earn its place? It needs a new endpoint and codec-compatibility logic,
   against a saving that is only real if screens genuinely play library MP4s without help. Worth
   measuring one before building it — the pre-render was removed for exactly this kind of assumption.
2. Where does the kit's **backdrop** come from once there is somewhere to put it — a still, a video
   bed, or a generated visualisation? The renderer is the thing that would answer, and today the
   answer is "nothing".
3. `.kfa` duration currently comes from a legacy `.kft` sidecar only
   (`KaraFunMediaProvider.cs:1266-1273`), not from the container. Does the rendition carry duration
   so the host stops asking the file twice?
4. Does a rendition need to say **why** it refused to be direct, so the console can explain a song
   that fell back to an encode?
5. What happens when the display changes mid-song? Today a connect triggers a reload; with renditions
   that becomes "re-render for the new target", which is a better-defined act but a new one.

## Staging

Each step is separately shippable, and the first two are worth doing whether or not the rest happens.

1. **Introduce the contracts and the fallback only.** `IMediaRenderer`, `MediaRendition`,
   `MediaRenderRequest`, and a `StreamingMediaRenderer` that wraps `HlsMediaStreamService` and
   reproduces today's behaviour exactly. Nothing else changes; the suite should be green untouched.
2. **Move the kit onto it.** The plugin implements the renderer, `StemsOf` and the `DescribeStems`
   gating in `PlaybackService` both disappear into it, and a kit on a mixing screen stops running
   ffmpeg at all — which is the original goal.
3. **Give CDG its own renderer.** `CompactDiscPlusGraphicsRenderer` claims `.cdg` and **inherits the
   encode** from `StreamingMediaRenderer` rather than reimplementing it: subcode graphics still have
   to be decoded into a picture and ffmpeg is what does that. It exists anyway, because the rules
   that belong to the format need somewhere to live — and because the day a screen draws a CDG
   itself, the way a kit's stems are now mixed there, that body is what changes and nothing above it
   notices.
   - The first rule it owns: **a `.cdg` with no `.mp3` beside it is an invalid state, not a quiet
     song.** It was importable and playable, reaching the room as a silent stream with a warning in
     a log nobody reads. It now fails with `KH-CDG-NO-AUDIO`, naming the file to put back.
   - What was *not* moved: `-r 30`, the companion input and the output-seek rule stay in
     `BuildArguments`. They are how ffmpeg is driven, and the alternatives were duplicating that
     method or pushing frame rate and seek mode through `IMediaStreamService`, which is an
     `Abstractions` contract a plugin compiles against. The renderer owns whether the media is
     *valid*; the encoder owns how it is *encoded*.
   - Naming the seek rule did find a bug on the way: `seekOnOutput` keyed off
     `companionAudioPath is not null` while its own comment explained it was about CDG's stateful
     decode. Now keyed off `IsGraphicsOnly`, with a regression test — defence in depth, since the
     renderer refuses that pairing before it reaches the encoder at all.
4. **Decide `DirectMediaRenderer` on evidence** — open question 1 — and only then build the
   endpoint. **Not started**, deliberately: it needs a new LAN-facing route serving library files,
   against a saving that is speculative until someone confirms screens play library MP4s unaided,
   and assuming that is how the pre-render came to be built and then deleted.

## What this should answer without further argument

1. Where does a new format's handling go, and what does its author implement?
2. Who decides whether ffmpeg runs, and what do they know when they decide?
3. What does a display that cannot mix, or cannot decode, get instead?
4. Why is the CDG seek rule not in the shared argument builder?
5. What breaks first if two renderers claim the same file?
