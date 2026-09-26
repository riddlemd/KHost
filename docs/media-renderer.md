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

That spread has a measurable cost right now. A stems-only format played on a screen that mixes takes
the stem branch in the page and never constructs hls.js, so the playlist is never fetched — while the
host runs a full libx264 + AAC encode of the whole song for a consumer that does not exist. Nothing in
the pipeline can say "this one needs no encode", so every format gets the same treatment.

## The three cases, side by side

Three formats were surveyed specifically because they disagree on every axis that matters.

| | **CDG + MP3** | **MP4** | **a stems format** |
|---|---|---|---|
| Files in | two — graphics plus a sibling `.mp3` | one | one container, demuxed to N stems |
| Picture | **synthesized** — ffmpeg decodes the subcode graphics | already encoded in the file | **none at all** (see below) |
| Audio | the companion `.mp3`, ffmpeg input 1 | embedded in the container | N Ogg Vorbis stems |
| Words | burned in | burned in, or none | `ITimedLyricsProvider`; burned in by the host for a display that asks |
| Needs a host encode? | **always** | no, unless shifted | only when shifted, or the target cannot mix or wants words burned in |
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
  video, which is the stems-only case leaking into the general path.
- **The stems format's remux** — *gone.* The plugin used to stream-copy its stems into a video-less
  container purely so `HlsMediaStreamService` had something to open. The host now reads the stems
  themselves (`OpenStemsAsync`), so a stems format builds no second container at all.

### A stems-only format has no picture, and that is not an oversight

A screen that mixes draws the words itself over nothing. A display that cannot draw them asks for
`RenderTarget.BurnLyrics`, and the host's encode paints them in: over one of the venue's chosen
song backgrounds when there is one, over black otherwise. That is the host's rule for *any* song
with timed words and no picture of its own — no renderer paints anything, and a plugin that ships
such a format supplies `TimedLyrics` and answers with its stems as always; the host encodes them
with the words over that background.

## What the differences demand of the interface

Lining the three up produces five things the abstraction has to express, and today's pipeline can
express none of them.

1. **A rendition is not always a stream.** It may be a URL to encode into, a URL to a whole file, or
   a set of parts with no single URL at all. `LoadMediaCommand.StreamUrl` being `required` is the
   current shape's way of insisting otherwise.
2. **Whether a seek needs a rebuild.** An HLS stream is cut at the playhead, so its zero is
   `StreamStartOffset` and seeking outside the encoded range means reopening. A whole MP4 or a set
   of stems is the entire song and a seek is free. That distinction is implicit in `StreamStartOffset`
   today; with three renderers it has to be stated.
3. **What the target can do.** Whether an encode is needed depends on the display, not only the file:
   the same stems-only format needs one for a Chromecast and none for a screen. So the request carries the target's
   capabilities, answered by the connected provider's `DescribeTarget()` rather than read off a
   device flag on the side.
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

    /// <summary>Parts the display mixes itself; empty for everything that does not ship separate stems today.</summary>
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

Returning **null** means "nothing to do here" and falls through to the fallback renderer.

**A renderer supplies stems; the host decides what the target needs.** A stems format answers with
its stems for every target, key and tempo, and makes no mixing decision. `MediaRendererService`
hands stems alone straight to a target that `MixesStems` when no key or tempo change and no
`BurnLyrics` was asked for. Anything else — a Chromecast, a key change on a screen, a device
wanting words in the picture — goes to `StemMixdown`, and `HlsMediaStreamService.OpenStemsAsync`
encodes one stream: every stem an input (read off disk when it sits in a host session, fetched
over http otherwise), mixed at its `StemSource.Volume` through the same per-voice graph, then keyed,
retimed, and — for `BurnLyrics` — laid under the words over the venue's background or black. The
encode adopts the renderer's session, so closing it sweeps the stems too. A key, tempo or mix change
re-renders at the playhead and goes through the same decision.

### The renderers

| Renderer | Claims | Answers |
|---|---|---|
| `StreamingMediaRenderer` (fallback, keyed) | everything | the HLS session it builds today; owns the CDG rules |
| `DirectMediaRenderer` | containers the target plays as-is, unshifted | a URL to the library file, `SeekableInPlace` |
| a plugin's renderer for its own stems format | the format's own extension | its stems, for every target; the host encodes them where needed |

The fallback is registered **keyed**, exactly as `FfprobeMediaProbe` is behind
`MediaProbeService.FallbackKey`, so it never appears in the `IMediaRenderer` enumerable — it claims
every file and would win every race.

### Why not key off the extension

It is close, and it is nearly enough. A renderer declares its own extension, the host builds a dictionary,
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
- **It costs a plugin nothing either way.** `bool CanRender(string p) => IsMyFormat(p);` is the same
  line as declaring an extension list, so the flexibility is free. Most renderers really will be
  that one line.

The usual argument for a dictionary does not apply: `CanRender` is asked **once per song**, not per
queued turn like `Claims` and `CanProbe`, so iterating every renderer costs nothing.

Collisions are answered the way `IMediaProbe` answers them — first claim wins, and the greedy
fallback is registered keyed so it never enters the race. An extension dictionary would not have
removed that problem, only renamed it to "two plugins both declared `.mp4`", resolved by insertion
order with nothing to read.

### Why not key off `MediaType`

Because the type does not carry the distinction. `MediaFormats.TypeForFile`
(`KHost.Common/Media/MediaFormats.cs:58-74`) has no idea a plugin's own stems-format extensions
exist — a plugin claims those independently — and a plain `.mp4` comes back `Karaoke` or `Video` depending on a
flag the *caller* passes, not on anything about the file. An enum switch would therefore have to be
extended by the host every time a plugin brought a format, which is the opposite of the point.
Claim-by-file is the pattern this codebase already uses for exactly this reason.

## What this replaces

A stems format no longer needs `IPlayableMediaSource`: the host encodes from the stems a renderer
hands back. The contract stays, for a format whose file the host's encoder cannot open and that a
cheap transform turns into one it can.

`HlsMediaStreamService` survives, wrapped — the same way `IScreenServer` survived behind
`LocalScreenDisplayProvider`. Its ffmpeg argument building is not the problem; being the only answer is.

## Risks

- **This is not the deleted render path, and it will be mistaken for it.** `AGENTS.md` says flatly
  "There is no host-side render path" and records why `IMediaPreparer` and `PreparedMediaService`
  went: they produced a cached artifact ahead of time and grew eviction, progress reporting and state
  that outlived the song, for ~0.1–0.2s of start latency and no reliable CPU saving. This is a
  per-play router that answers "how does this file reach this display", produces nothing that
  outlives the session, and has no cache. **If that distinction is not written into `AGENTS.md` at
  the same time, someone deletes this in six months and cites that line correctly.**
- ~~**The name collides.**~~ Avoided: the plugin implements `IMediaRenderer` on its own extension
  type, and no painting code of its own is left beside it — the host paints burned-in words.
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
2. ~~Where does a stems-only format's **backdrop** come from?~~ Answered for burned-in streams:
   the venue's chosen song background, else black. A screen that draws its own words still shows
   them over nothing.
3. ~~A remuxed stems container's duration~~ Moot: no stems container is built any more. A burn-in
   over stems probes the stems themselves for how long to paint.
4. Does a rendition need to say **why** it refused to be direct, so the console can explain a song
   that fell back to an encode?
5. What happens when the display changes mid-song? Today a connect triggers a reload; with renditions
   that becomes "re-render for the new target", which is a better-defined act but a new one.

## Staging

Each step is separately shippable, and the first two are worth doing whether or not the rest happens.

1. **Introduce the contracts and the fallback only.** `IMediaRenderer`, `MediaRendition`,
   `MediaRenderRequest`, and a `StreamingMediaRenderer` that wraps `HlsMediaStreamService` and
   reproduces today's behaviour exactly. Nothing else changes; the suite should be green untouched.
2. **Move the stems-only format onto it.** The plugin implements the renderer, `StemsOf` and the
   `DescribeStems` gating in `PlaybackService` both disappear into it, and that format on a mixing
   screen stops running ffmpeg at all — which is the original goal.
3. **Give CDG its own renderer.** `CompactDiscPlusGraphicsRenderer` claims `.cdg` and **inherits the
   encode** from `StreamingMediaRenderer` rather than reimplementing it: subcode graphics still have
   to be decoded into a picture and ffmpeg is what does that. It exists anyway, because the rules
   that belong to the format need somewhere to live — and because the day a screen draws a CDG
   itself, the way a stems format's parts are now mixed there, that body is what changes and
   nothing above it notices.
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
