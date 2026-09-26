# AGENTS.md

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Photino screen app. Solution: `KHost.slnx` (no `.sln`).

Projects (`src/`): `Abstractions` (every interface, the shared models, and what a plugin is built against — MIT, no project refs, the bottom layer) ← `Common` (helpers over those contracts, MIT) ← `Domain` (services) / `DataAccess` (EF Core 10 + SQLite) ← `UserInterface` (Blazor Server) and `LocalScreen` (Photino video output), plus `IPC.SignalR` (UI↔Screen), `Secrets` (per-OS secret store behind `ISecretStore`; `Interop/` is ported from Git Credential Manager and kept textually close to upstream), `LrcLib`, `Telemetry`, `ServiceDefaults`/`AppHost` (Aspire), `tools/` (`KHost.CatalogSync`, the CLI that writes `plugin-catalog.json` entries), `build/` (`KHost.Analyzers`, a netstandard2.0 Roslyn analyzer referenced only at build time), and `tests/` (`KHost.UnitTests` — hermetic, no skips; `KHost.IntegrationTests` — needs ffmpeg/ffprobe, and the OS secret-store tests skip on any other platform).

## Commands

```bash
dotnet run --project src/KHost.UserInterface                # run the app (native Photino window)
dotnet run --project src/KHost.UserInterface -- --headless  # no window; console served at http://localhost:5251
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"    # build (redirected so VS's bin/ isn't locked)
dotnet test tests/KHost.UnitTests                           # --filter "FullyQualifiedName~Name" to narrow
dotnet test tests/KHost.IntegrationTests                    # drives real ffmpeg; fails without it (KHOST_SKIP_ENVIRONMENT_TESTS=1 to accept)

dotnet run --project tools/KHost.CatalogSync -- <owner/repo> # add a plugin's GitHub release to plugin-catalog.json

./build/pack-contracts.sh                                   # pack Abstractions + Common to the local NuGet feed
./build/check-secrets-drift.sh                               # diff src/KHost.Secrets/Interop against the pinned upstream commit; needs network
```

**Prefer `--headless` for testing.** The console is then an ordinary page at `http://localhost:5251`, so it drives with browser tooling and reads with the DOM instead of screenshot coordinate math — the Photino window reaches neither, and a LocalScreen window launched over it turns every later capture into a black rectangle. Only the window itself needs the windowed run: native chrome, `SetSize`, and the appliance lockdown. Port 5251 is held by an exclusive `.instance.lock`, so stop one before starting the other.

SCSS compiles inside `dotnet build` (AspNetCore.SassCompiler) — no separate sass step. The build needs `node_modules` (`npm install`) for `copy:vendors`.

## Rules

- Interfaces in `src/KHost.Abstractions` (`Services/`, `Repositories/`, `Models/`); implementations in `src/KHost.Domain` or `src/KHost.DataAccess`. The rule is about what a plugin builds against, so an interface a plugin must *not* reach sits with its implementation instead — `IQrCodeService` takes an owner id, and a plugin able to pass any owner could register over another's QR code without either noticing. Register in the project's `ProjectExtensions` (`AddDomain()` / `AddDataAccess()`); UI-only services in `Program.cs`. All domain services are singletons — guard mutable state with `SemaphoreSlim`.
- A helper both the host and a plugin would want goes in `KHost.Common`, not `Abstractions`: it is MIT on purpose, so a plugin author may use it without taking PolyForm code into what they redistribute. `Common` is for helpers *over* the contracts — string folding aids, formatting, list surgery, the shared drop-position mechanic. A contract, a model or anything `Abstractions` itself needs belongs in `Abstractions`, which references nothing. `Abstractions` declares, it does not compute — see **No static methods in Abstractions** below. Group by area under `Common` (`Media/`, `Plugins/`, `Discovery/`) rather than dropping types in its root, and mirror that in the tests. Name its methods for what the call site needs to read, not for what the class already says: a plugin author sees `StreamRate.FromTempo(t)` and `AudioLevels.ClampVolume(v)` without this repo's context, so `For` and `Clamp` are too thin — `PluginRid.MatchesThisHost` names what it matches against, and `int.CentsToCurrencyString()` names the unit the receiver is in. The one exception is a member that exists to fill a BCL gap (`IList<T>.FindIndex`), where the familiar name *is* the point.
- No "gate" services: behaviour that guards a call lives on the service that owns the call (enqueue rules go in `PerformanceService.CreateAndEnqueueAsync`, not an `IEnqueueGuard` around it). `IMediaGateService`/`IMediaProbeService` are routers, not guards: they answer which plugin owns a file, and the rule itself lives in the plugin.
- New repositories/services copy the shape of an existing one: repositories extend `BaseRepository<T>` and implement `SortColumns` / `ApplySearchFilters`; services extend `BaseService` (or `BaseRepositoryService<,>` for CRUD).
- In repositories, `using var context = await ContextFactory.CreateDbContextAsync();` per operation — never store a context.
- Services announce, they do not raise events. There is no `StateChanged` and no `IKHostService`: a service that has something to say takes `IMessageBroker` in its own constructor (never through `BaseService`, which carries only `ILogger`) and calls `Broker.Announce(new ThingChanged())`. Messages are empty records in `KHost.Abstractions.Messaging.Messages`, one per service, named for the fact — see **Messaging** below.
- Member order: fields → events → properties → public → protected → private → nested types.
- Every `Task`/`ValueTask`-returning method ends in `Async` — enforced by reflection in `AsyncNamingConventionTests`; a new project must be a `ProjectReference` of `KHost.UnitTests` to be covered.
- Method names that cross a string boundary (`[JSInvokable]` called from JS, SignalR hub methods invoked by name) break silently when renamed: pass the name as `nameof(...)` from C# and take it as a parameter in JS (see `SingerQueuePanel` / `sortable-interop.js`, `ScreenClient` / `ScreenHub`).
- Library/users/groups persist in SQL; queue and venue state in the JSON cache (`ICacheService`, `./cache/`).
- Dialogs go through `IInteractionDispatcher`, which resolves `IInteractionHandler<TReq, TRes>` from DI; handlers bridge dialogs into awaitable calls with `TaskCompletionSource` and are registered in `Program.cs`.
- `KHost.Abstractions` and `KHost.Common` are MIT; everything else is PolyForm Shield (`LICENSE`, and each MIT project's own `LICENSE`). `LicenceBoundaryTests` enforces it: an MIT project may reference only MIT projects, and must declare `PackageLicenseExpression` and ship a `LICENSE`. Note the compiler catches only the circular case — a reference to a leaf like `KHost.LrcLib` builds fine and breaks the licence silently, which is what that test is for. `KHostException` lives in `Abstractions` with the interfaces it is thrown across — it is the only way a plugin can report a failure the host can act on, so it has to sit where a plugin can reach it.
- Do NOT commit unless explicitly asked.

## No static methods in Abstractions

`KHost.Abstractions` holds data and contracts. A static method there is a function, and a function
is behaviour that belongs in `KHost.Common` where a plugin may also reach it. This is enforced at
build time, not by review: `build/KHost.Analyzers` raises **KH0001** at the declaration, and it is
fatal in that one project via the `.editorconfig` beside its `.csproj`.

- The analyzer ships `DiagnosticSeverity.Hidden` so it can be referenced anywhere without biting;
  the `.editorconfig` line is what makes it an error. `NoStaticMethodsInAbstractionsTests` checks
  both halves of that wiring, because losing either one disables the rule with a green build.
- The reference is `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` — build-time only,
  so no Roslyn assembly reaches what a plugin redistributes. `LicenceBoundaryTests` skips analyzer
  references for that reason.
- Exempt because the language requires `static`: `Main`, operators and conversions, static
  constructors, `[ModuleInitializer]`, and extension methods. Static *fields* and *properties* are
  not methods and are untouched — `ScreenCapabilities.None`, `MediaSearchOptions.Default` and
  `PluginRid.Current` all stay. `#pragma warning disable KH0001` is the escape hatch, and wanting
  one is usually a sign the member belongs in `Common`.
- Precedents for the split live in `Common/Media/`, `Common/Plugins/`, `Common/Authentication/`
  and `Common/Repositories/`.

## Finding things on the network

**A .NET process on macOS cannot send multicast.** A send to `224.0.0.251` fails with
`EHOSTUNREACH` ("No route to host") while a unicast to the very same host succeeds a millisecond
later. That asymmetry is how macOS reports a local-network denial, and a command-line binary is
denied *silently* rather than prompted — `dotnet` never even appears in Privacy & Security → Local
Network. Every managed mDNS stack is therefore blind on macOS, and none of them say so: Zeroconf
returns an empty list in half a second from a five-second scan, which reads as "nothing out there"
rather than "I could not ask".

`Common/Discovery/BonjourBrowser` is the way round it — macOS's own daemon, reached over XPC rather
than the wire, so the denial does not apply. It sits in `Common` because **any** plugin reaching a
network device meets this, and sharing it costs nothing: it is BCL plus a `DllImport` of
`libSystem`, so `Common` keeps its no-package-dependencies property.

- `BonjourBrowser.IsSupported` is **macOS only**. Everywhere else a managed stack works and should
  be used; the Cast plugin keeps Zeroconf for Windows and Linux and branches on this.
- Zeroconf does ship a Bonjour browser, but only in its `ios` and `maccatalyst` targets. Inheriting
  it means multi-targeting to an Apple TFM, which forces a platform-specific plugin build and a
  second catalog release — `Rid` is meant to stay blank.
- A sweep that finds nothing must **say so**. "Blocked", "empty room" and "working fine" otherwise
  look identical in a log, which is the whole reason this cost a day to find once.

## Messaging

`IMessageBroker` (`KHost.Abstractions/Messaging/`) is how services and components hear about each other. A plugin can subscribe to what the show is doing, `Abstractions` being what it builds against.

- **`Announce(message)`** is fire-and-forget, for "this moved, redraw". **`await PublishAsync(message)`** waits for every handler and is for the case the publisher's next decision depends on the outcome.
- Handlers run **one at a time, in subscription order** — what one does decides what the next may do. A handler that throws is logged and skipped: a broken subscriber must not stop the queue reaching the next singer.
- Routing is on the message's **runtime type**, and exact — a handler for a base type is not called for a derived one.
- Subscriptions return `IDisposable`. Hold them in a `SubscriptionSet` and dispose it; a missed unsubscribe keeps a Blazor component — and its whole circuit — alive on the broker.
- Never take a lock around a publish. `ScreenServerService` raises `ScreenConnected`/`ScreenDisconnected` only after releasing its own lock, for the same reason. The handlers are still `_ = Task.Run(...)` because the event is a plain `EventHandler` on the hub's thread: awaiting there directly would be `async void`.
- Components `[Inject] IMessageBroker Broker`, subscribe in `OnInitialized`, dispose the set in `Dispose`.

Four things deliberately stay plain C# events, and should stay that way: LocalScreen's `IMediaPlayer` and `IScreenClient` (a separate process — the broker is in-process and SignalR is the transport), `IDialogService.ShowRequested` (a request with a payload and one legitimate subscriber, not a notification), `IPlaybackService.PositionChanged` (twice a second for a whole night; it says only that `Position` moved, so take it to redraw a playhead and nothing else), and `IPlaybackService.PerformanceEnded` (a gap with a payload the subscriber fills: a request, not a notification).

## Vocabulary

- **Renderer** means only an `IMediaRenderer` — the thing that decides what a display gets when a
  song starts. Its answer is a **rendition**.
- **Encode** is ffmpeg producing a stream; **remux** is a container change with no re-encode. Never
  "transcode".
- **Burn in** (verb) and **burned-in** (adjective) mean words drawn into video frames — not "burn-in"
  as a noun, and not "burnt".
- **Mix** is combining stems: the host's mix (ffmpeg) or the screen's mixer (WebAudio). **Paint** is
  drawing a video frame for an encode; **compose** is assembling painted frames and audio into one.
- **Draw** is what a display does to present something — the lyrics overlay, cards, the progress
  bar. "Render" as a verb stays only for an `IMediaRenderer` producing its rendition, or for the
  separate Blazor/HTML sense (`OnAfterRender`, "re-render", bunit's `Render<>`) — never for a
  display drawing something.

## What a plugin can reach

A plugin's entry point is constructed with `ActivatorUtilities.CreateInstance` against the host
container, so it takes whatever it needs from `KHost.Abstractions` in its own constructor — the same
service interfaces the host uses. There is deliberately no facade. `IPluginContext` carries the
plugin's own manifest and settings, and the calls where the *host* supplies the identity so a plugin
cannot name another's: its secrets, and the QR code it offers the screens.

- Downloading media for the queue goes through `IMediaAcquisitionService`. It owns three rules
  nothing else may re-implement: an import is idempotent by `FilePath`, the media row's status and
  the `IDownloadsService` entry move together, and `DiscardImportAsync` deletes only a row still in
  `Downloading`. Enqueuing is not on it: compose `ISingerQueueService.SelectedUserId` with
  `IPerformanceService.CreateAndEnqueueAsync`; folding the pair into either closes a constructor
  cycle.
- **`Processing` is the second half of `Downloading`, not a different state.** A provider that has
  to turn the bytes into something playable calls `BeginProcessingAsync(mediaId)` once the download
  is in and verified. It is the only transition a plugin may make besides the three settles, moves
  only a `Downloading` row, and leaves the `IDownloadsService` entry alone; the entry carries a
  `DownloadPhase` so the Downloads page names which half a percentage measures, and changing phase
  clears the progress. Ask `MediaStatuses.IsAcquiring()` (`Common/Media/`) rather than
  `== MediaStatus.Downloading`, or a row in phase two is stranded; `MediaStatuses.Acquiring` is the
  same question as data for EF. `FailImportAsync` takes an optional reason the page shows — a line
  a host can act on, never a stack trace. `DiscardImportAsync` reads `FilePath` itself: it deletes
  the row only when nothing is on disk and keeps it as `Broken` when a file outlived the cancel, so
  a partial is never left with no row pointing at it.
- A plugin that needs to **ask the host for a value** injects `IInteractionDispatcher` and sends a
  `TextPromptRequest`. The line is **settings versus secrets**: nothing from that round trip reaches
  `plugins.json`; a plugin that keeps what it collected uses `IPluginContext.SetSecretAsync`. Never
  keep a raw password — hash it before storing. `Secret: true` masks the input on screen; it says
  nothing about storage.
- **A plugin that needs to show a *list* sends a `ShowPluginTableRequest`.** A plugin ships no
  markup — its assembly is never handed to the renderer, the same reason a manifest names an icon
  instead of supplying one — so it describes a table and the host draws it.
  - It names the `Title` and the `PluginTableColumn`s, which do not change, and everything that
    does comes back from **one** `LoadAsync`: the rows, the buttons above the table, and the line
    shown when there are none. Reading those through separate delegates is how a stopped search
    ends up with a "Searching" button over an empty table.
  - `PluginTableAction.PerformAsync` is a **delegate, not a key the host dispatches back** — the
    same shape as `MediaProviderAction`. The plugin closes over whatever the action needs, so the
    host keeps no map from strings to behaviour and never learns what a row means.
  - The dialog re-reads after every action, and whenever **`PluginTableChanged`** is announced. A
    plugin whose list fills in on its own — a network sweep — must announce it, or an open table
    sits stale; the dialog is generic and hears nothing transport-specific.
  - Reached from a Plugins-page button, so the plugin implements `IPluginButtonHandler` too and
    both live on the one extension object. `DescribeButton` is what lets the row report state
    without the host opening anything.
- **Buttons on the Plugins-page row** are declared in the manifest (`PluginButtonDefinition`) and
  implemented by `IPluginButtonHandler`. The host runs `InvokeButtonAsync(key)` then re-reads
  `DescribeButton(key)`, so one button can toggle its own label, hide, or disable itself. Reached by
  plugin id through `IPluginButtonService`.
- **Gating content is the plugin's job, not the host's.** `IMediaPlaybackGate` exists so a
  subscription provider can honour its own terms, and for nothing else. The host routes a question
  to whichever plugin owns a file and does what it answers. Do not add host-side enforcement, and
  do not try to make it airtight against a host who owns the machine.
  - **One verdict, asked at every moment.** `CanAsync(MediaAction, Media)` takes `Queue`,
    `Render` or `Play`; a provider whose answer never varies ignores the argument. Call sites:
    `PerformanceService.CreateAndEnqueueAsync` (Queue) and `PlaybackService.LoadAsync` (Play).
    `Render` is **raised by nothing right now** — it was the pre-render, which is gone — and stays
    in the enum because a plugin compiles against it and the native render path will want it back.
    `Queue` and `Play` may raise a sign-in first; refusing at `Queue` means a host learns before the
    singer is at the microphone.
  - **Ownership is asked by name first.** `IMediaGateService` asks every gate's `Claims(path)` before
    reading any tag, because the path is free and the tag opens the file. Only when nothing claims
    the name does it read the container tag `IMediaPlaybackGate.MetadataTag` (`khost_provider`),
    which names one gate to ask. The tag read is skipped for a file not yet on disk — the gate is
    asked at enqueue, while a provider's own download may still be arriving. A provider whose
    container has an extension nothing else produces should gate on the extension alone. `Claims`
    is false by default and must answer from the **path alone**: it runs for every queued turn on
    every reconcile. Writing the tag onto a render needs ffmpeg's `+use_metadata_tags` movflag.
  - The gate is on the **source container**, not the render, which is a temporary file the library
    never points at. A render's odd extension is obscurity so licensed content does not open on a
    double-click, not protection — KHost reads media by content.
  - A block refuses the action like a non-Ready row and flashes the gate's reason. A gate that
    **throws** is logged and the action proceeds. The check runs on every load, so keep it an
    in-memory answer unless the content is worth a round trip.
- **A plugin that ships its own container describes it, through `IMediaProbe`.** ffprobe reports a
  container it cannot open as "Invalid data found", indistinguishable from a file with nothing in it.
  - `CanProbe(path)` claims from the path alone; `ProbeAsync` returns a `MediaProbeResult` —
    duration, audio tracks, container tags. **Null and empty differ**: null is "I could not tell",
    empty is "I looked and there is nothing".
  - **The probe returns facts; the asking service keeps its policy.** `AudioTrackService` decides
    that one track is nothing to balance and a set with no music track is not worth offering. Tracks
    come back already roled — a plugin knows its own stems.
  - `IMediaProbeService` routes to the first plugin that claims the file, else `FfprobeMediaProbe`.
    The fallback is registered **keyed** (`MediaProbeService.FallbackKey`) so it never appears in the
    `IMediaProbe` enumerable — it claims every file. A plugin that throws deciding is skipped; one
    that throws reading its own format answers null rather than falling through.
  - Nothing is cached: a file swapped under an unchanged path must be re-read.
- **Two questions a provider answers about a file, and they are not the same question.**
  `IMediaPlaybackGate.Claims` asks who *owns* it, `IMediaProbe.CanProbe` who can *read* it. A
  provider with one closed container answers both with the same extension check, which makes them
  look redundant; a plugin that gates content the host reads perfectly well claims the gate and not
  the probe. Answer each for what it asks. Both must answer from the **path alone** — each runs for
  every queued turn on every reconcile, and opening the file turns a bulk enqueue into thousands of
  reads. `Claims` has a **default body**, which is behaviour in `Abstractions` the KH0001 analyzer
  cannot see; it exists so a plugin with no opinion loads unchanged. A deliberate exception, not a
  precedent.
- **There is no host-side *pre*-render.** `IMediaPreparer` and `PreparedMediaService` are gone, along
  with `PerformancePreparation`, the copy plan and the keyframe-cadence rules built on them:
  measured against streaming, the pre-render bought ~0.1–0.2s of start latency and no reliable CPU
  saving. A format the host cannot play is therefore **unplayable** until the native render path
  lands — see `src/KHost.LocalScreen/RESEARCH.md`.
- **`IMediaRenderer` is not that, and will be mistaken for it.** It turns one file into something a
  display can play, and it is asked **once, when a song starts**. It produces nothing the stream
  session does not sweep, caches nothing, reports no progress, and holds no state that outlives the
  song — which is every property that made the pre-render worth deleting. The full shape and its
  reasoning live in `docs/media-renderer.md`; this is the short form.
  - **It answers with what to play, not always with a stream.** A `MediaRendition` carries a URL to
    play end to end, or the separate `Stems` a display mixes for itself, or both. `StreamUrl` on
    `DisplayLoad` is nullable for exactly this: a stems-only format on a screen that mixes runs
    **no ffmpeg at all**, where it used to encode a whole song for a consumer that never fetched it.
  - **Claim by file, not by extension or by `MediaType`.** `CanRender(path)` and a keyed fallback,
    the same shape `IMediaProbe` uses — `MediaFormats.TypeForFile` has never heard of a plugin's own
    stems-format extension, and a plain `.mp4` is `Karaoke` or `Video` depending on a flag the
    *caller* passes. Unlike `IMediaPlaybackGate.Claims` and `IMediaProbe.CanProbe`, which answer
    from the path alone because they run for every queued turn on every reconcile, this is asked
    once per song and **may open the file** — which is what would let a renderer decide on a
    container's codecs rather than its name.
  - **Returning null means "nothing better for this target"**, and falls through to
    `StreamingMediaRenderer`, which claims everything and encodes as the host always has. That is
    how a stems-only format reaches a receiver: the plugin sees a target that cannot mix, declines,
    and the fallback resolves its remuxed container through `IPlayableMediaSource` exactly as before.
  - **The target is part of the question.** `RenderTarget.MixesStems` says whether the one
    connected display mixes for itself; a device hearing the host's own mix needs the encode, so
    offering it stems would be waste.
  - **A renderer may inherit the encode rather than replace it.** `CompactDiscPlusGraphicsRenderer` claims
    `.cdg` and derives from `StreamingMediaRenderer`, because subcode graphics still need ffmpeg to
    become a picture. It exists so the rules that belong to the format have a home: the first is
    that **a `.cdg` with no `.mp3` beside it is invalid, not silent**, and it now fails with
    `KH-CDG-NO-AUDIO` instead of encoding a silent song. The split is that a renderer owns whether
    the media is *valid*, and `BuildArguments` owns how it is *encoded*.
  - Everything still streams through `HlsMediaStreamService` unless a renderer says otherwise, and
    its ffmpeg argument building was never the problem — being the only answer was.
- **`IDisplayProvider` is a transport to somewhere the song comes out: transport and control,
  nothing drawn.** It finds such places, connects to one, hands it what to play and drives transport
  on it. It does not decide what the show is — it is told. The full shape and its reasoning
  live in `docs/display-provider.md`; this is the short form.
  - **The screens provider is core logic, not a plugin.** LocalScreen reaches the host through a
    provider the host itself registers, travelling the same path a plugin's display travels.
    `PluginLoader` must not bind it and it must never appear on the Plugins page. Chromecast is the
    plugin-supplied one.
  - **One display, full stop** — the local screen *or* a receiver, never both, and never two of
    either. There is no multi-screen or multi-device seam anywhere in the host: `ConnectedDeviceId`,
    `SessionId` and every argument-free member address the one device a transport drives, and a
    plugin display handles its own communication with it. The rule is enforced in two places: a
    provider refuses a connection to a different device rather than replacing the one it has
    (`LocalScreenDisplayProvider.ConnectAsync`), and picking a display disconnects every other provider
    first (`SettingsButton.SelectDisplayAsync`, and the "Launch Screen" confirm in
    `DialogService`, which goes the same way). `PlaybackService` and the break music provider ask
    `ConnectedDisplay.Find` for the one connected provider — several are registered at once, so
    finding it stays.
  - **The interface has no drawing members.** A plugin writes discovery, connection,
    `LoadAsync(DisplayLoad)`, play, pause, stop and seek; `DescribeTarget` (`RenderTarget.None`),
    `SetStemVolumeAsync` (false), `SetVolumeAsync` and the second channel have default bodies —
    the same deliberate exception `IMediaPlaybackGate.Claims` is. Its arguments are
    `Abstractions` models (`DisplayLoad`, `StemLevel`, `BackgroundLoad`), never the local
    screen's wire commands.
  - **What a device takes is the provider's answer, not a flag on the device.** `DisplayDevice`
    carries only `SupportsAudio`, `SupportsVideo` and `SupportsFade`. Lyrics are the one overlay
    worth compositing — fixed for the whole song — so a display that cannot draw them asks for
    `RenderTarget.BurnLyrics`; anything else it cannot draw, it simply does not draw.
  - **`SetStemVolumeAsync` answers whether the level landed.** False means the display cannot
    ride it, and the host rebuilds the stream at the playhead with the new mix baked in. It is
    called only while the loaded song carries stems.
  - **The wire to the local screen is not a contract.** `IScreenServer`, `IScreenClient`,
    `IScreenProvider`, `IScreenKeyStore` and every command and state type live in
    `KHost.IPC.SignalR.Contracts`, which `Domain` references; nothing a plugin builds against
    names them. `LocalScreenDisplayProvider` maps the host's calls onto its own commands.
  - **The provider owns presentation; the host only supplies data and services.** A display
    provider talks to some service or hardware the host may or may not control, and the local
    screen app is simply the device behind one of them. `LocalScreenDisplayProvider` hears what
    moved, pulls the whole current state of whatever that message drives and decides how it looks,
    reading **only what a plugin display can read**: `IPlaybackService.CurrentProgram` (idle, a
    song, or an ad still; announced by `PlaybackChanged`, so compare by value), `IUpNextService` +
    `UpNextChanged`, `IQrCodeOfferService` + `QrCodeOfferChanged`, `NextSingerAnnounced`,
    `IBreakMusicService`, `ITimedLyricsService` and the venue's settings. It composes the marquee
    itself (`BuildMarqueeAsync`), with the wording rules in `Common/Display/MarqueeText` so a
    plugin display drawing one says the same thing. Encoding the QR SVG, filling an unset placement
    and building the screen's commands stay inside it. Registering a code (`IQrCodeService`, which
    takes an owner id) stays Domain-only. `IPlaybackService` takes every display, so a provider
    resolves it on first use, never in its constructor.
  - **`UpNextChanged` is announced from one place, `UpNextService`.** It hears `SingerQueueChanged`,
    `PerformancesChanged`, `PlaybackChanged` (only when the singer at the mic moved) and
    `SelectedVenueChanged` (only when `AllowAliases` moved), and settles for 50ms so a stop — the
    playback, the dequeue and the rotation — is one announcement. Producers never announce it.
    The marquee names exactly the singers `IUpNextService` reads, so the two cannot disagree.
  - **`DescribeTarget()` is how a display says what to render for it.** `PlaybackService` asks the
    connected provider on every load; a throw or a null is `RenderTarget.None`. The default body is
    `RenderTarget.None`: one mixed stream, no burned-in words. `RenderTarget.BurnLyrics` is a request
    a renderer **may** honour — a renderer that paints lyrics into the picture can,
    `StreamingMediaRenderer` ignores it — and one that cannot returns its normal rendition.
    The local screen overrides it: stems, no burned-in words.
  - **An ad still's `ImageUrl` is reachable like a stream URL**: the same base address, under
    `/media`, which answers off-box. It may name loopback, so a provider for a device elsewhere on
    the network swaps in a LAN address exactly as it does for `StreamUrl`.
  - **`SupportsFade` is the one capability the host acts on for itself.** `StopAsync` *waits out*
    the fade it asks for, so a device that cuts dead — a receiver, which has no mixer of the host's
    to ride down — would otherwise buy the room that many seconds of silence before the queue moved
    on. A display that cannot fade means the stop is instant, the same reasoning as a paused
    stop being instant. A device that has not listed itself yet is taken to fade: over-waiting is a
    pause nobody hears, under-waiting cuts a song off mid-word.
  - **A screen is local only** — launched by the host on its own machine. So `discovery` is two acts
    under one name: a real network sweep for Cast, and "open one" for screens. **`SearchesForDevices`
    is which one a transport does**, and it is how the console decides what to offer: a header that
    says "search for devices" must not launch a screen, and one with nothing but the screens behind
    it must not offer the search at all. True by default, a plugin's transport being nearly always
    a sweep.
  - **There are no roles and no sync.** No audio screen, no primary, no timeline and no per-screen
    audio or video override: with one display there is nothing to choose between and nothing to
    steer onto anything else. The display that is up defines the song's clock: every provider
    raises `PlaybackStatusChanged` with its own timestamped position, and the host trusts only the
    report from whichever one is connected — a screen's own state reports reach `PlaybackService`
    the same way a receiver's do, through `LocalScreenDisplayProvider` translating them, not a side
    channel. Nothing is ever corrected towards anything. The venue's volume is applied by
    `LocalScreenDisplayProvider` on connect and on a venue edit.
  - **Covering a rebuild is the transport's business, not the host's.** Changing key, tempo or the
    mix reopens the stream at the playhead, and the host resumes there and skips nothing. It used
    to skip forward by however long the rebuild took, since the room heard on from the old stream
    meanwhile — but the skip lands on data ffmpeg has not written yet, so the element takes over
    with barely a frame buffered, sounds for an instant and then starves. Half a second of silence
    mid-song costs far more than a sliver heard twice. A screen covers the window itself, keeping
    its old element playing and handing over only once the new one has sound, which is exactly the
    mechanism a seek from the host defeats; a transport with nothing of the kind makes the
    difference up inside its own `LoadAsync`, where it knows what it is driving.
  - **The server registers one screen, and that is a constant, not an option.** A second screen is
    refused rather than quietly joining; a re-registration under the same id replaces the first.
    Every refusal in `TryRegisterScreen` is logged, because a turned-away screen shows "Lost the
    host" and waits, which from the room is indistinguishable from a crash. `IScreenServer` has one
    way to send, `BroadcastCommandAsync`, which reaches the screen that is up with a command
    signed under its own key.
  - The off-box HTTP surface — `LanAccessPolicy.IsMachineFacing`, the permissive CORS header, ranged
    GETs — stays host surface for future plugin displays rather than moving behind the Cast provider.
- **A plugin offers the screens a QR code; the venue decides whether it is drawn.** The manifest's
  `qrCode` is the standing registration, read without resolving the plugin so a venue can be set up
  before the show. `IPluginContext.RegisterQrCodeAsync` is the live one. The venue names **one**
  source in `Venue.Settings.QrCodeSource`, **none by default**; every other owner's code is held,
  not drawn, so switching mid-show is immediate. The owner is stamped from the loaded manifest,
  never passed by the caller. Placement is the venue's alone.
- A plugin adds importer extensions with a manifest `importFormats: [".ext"]`, read from
  `IPluginRegistry` without resolving the plugin and unioned with the built-ins for **loaded**
  plugins only, normalised to leading-dot lowercase. It is a *filter*: it says a row may be made,
  not that the host can play the file. A format the host cannot read still has nowhere to be turned
  into one — see the note on the render path above. The folder is never content-probed; the
  extension check is free.
- **A plugin extension type is one singleton across every extension interface it implements.** One
  object, one session key, so signing in on the button signs in the search and the gate. The bound
  interfaces are a hand-written list in `PluginLoader`, and leaving one off is **silent**;
  `PluginExtensionInterfaceTests` fails on any Abstractions interface the domain collects that is
  not listed, so the list maintains itself.

## The published contracts

`KHost.Abstractions` and `KHost.Common` are **NuGet packages**, and a plugin takes a
`PackageReference` to them rather than a `ProjectReference` into a checkout of this repo beside it.

- `<ContractsVersion>` in `Directory.Build.props` is the version of both. It moves whenever the
  shape an author compiles against changes at all, additions included; 0.x while the contracts
  still move.
- `PluginApi.CurrentVersion` (at **5**) is the runtime gate the host checks a manifest against, and
  it moves only on a break. Changing a method a plugin **calls or implements** is a break, including
  adding an optional parameter: the default compiles into the call site, and a changed implemented
  signature is a `TypeLoadException` at load. A new interface member with a **default body** is not.
  Do not reason from the published catalog about who implements what: the hand-installed plugin is
  the one being run. Widening a method also silently changes what `Received(1).Foo(id)` asserts in
  a plugin's tests, so assert the argument rather than the bare call.
- **A plugin excludes their runtime assets**
  (`<PackageReference Include="KHost.Abstractions" ExcludeAssets="runtime" />`): the host already
  has both in its default context and `PluginLoadContext.Load` returns null for anything that is,
  so a copy beside the plugin is never loaded. A plugin's *test* project takes them normally — it
  stands in for the host and has to load them.
- While the contracts are unreleased, `./build/pack-contracts.sh` packs both into a local folder
  feed. Register it once per machine:
  `dotnet nuget add source ~/.nuget/khost-local -n khost-local`.
- The script clears the matching entries from the global packages folder before packing, because
  NuGet never re-reads a version it has already extracted. Re-packing the same version is the
  normal case here, and without that step a plugin keeps building against whatever it restored
  first — which looks like the source change simply not taking effect.
- The analyzer reference in `KHost.Abstractions` carries `PrivateAssets="all"` so the package does
  not declare a dependency on `KHost.Analyzers`, which is not published.

## Plugin catalog and installs

The Available tab on the Plugins page installs from a published `plugin-catalog.json` (this repo's root,
served raw from `main`; `PluginCatalog:Url`). The catalog is the **trust root** — a plugin runs
in-process with the host's own access — so a release is only offered when it is served over https
and carries a `sha256`, and the download is hashed, the zip's entries are all checked for escapes
*before* one is written, and the manifest inside must declare the same id and
`ApiVersion == PluginApi.CurrentVersion`. `EntryAssembly` is checked to resolve inside the plugin
folder: `PluginLoader` hands that string straight to `LoadFromAssemblyPath`.

- Presentation metadata (repository, author, capabilities) belongs in the catalog, not
  `PluginManifest` — the manifest is MIT and a plugin builds against it, so adding a field breaks every external plugin's
  build, the same argument as `MediaSearchEntity`.
- Nothing installs into a running host. `IPluginStagingArea` parks payloads in `plugins-staging/`,
  a **sibling** of `plugins/` — `PluginLoader.Discover` treats every subdirectory of `plugins/` as
  a plugin, so nesting staging inside it renders a broken row. `<id>/` is a staged install,
  `<folder>.remove` a pending removal, `<id>.failed/` a payload the last start could not apply
  (with `error.txt` beside it, so it stops retrying), `.work/` download scratch — inside staging so
  the final `Directory.Move` never crosses a volume. Installs are keyed by id and removals by
  folder name, because two folders may carry one id: an install replaces the plugin wherever it
  sits, while a removal is a host pointing at one row on the Plugins page. A marker naming anything
  but a direct child of `plugins/` is ignored rather than followed.
- `ApplyPending()` runs from `AddPlugins` before `Discover`, so it predates the container: no DI,
  no logger, and a failure must never stop the app starting. It maps id → folders by reading each
  manifest, so an update replaces a plugin dropped in by hand under any folder name — and every
  copy of it, so a duplicate id does not outlive the install meant to replace it.
- **The catalog lists what the current host can install, and nothing else.** When
  `PluginApi.CurrentVersion` moves, every entry declaring the old one is rebuilt, re-released and
  the superseded entry **removed** — `LatestCompatibleRelease` matches the api version *exactly*,
  so an entry the gate has passed by is one no host will ever select again. Removing is the one catalog
  edit made by hand, since nothing about it asserts a checksum.
- **Add a release with the tool, never by hand**:
  `dotnet run --project tools/KHost.CatalogSync -- <owner/repo> [--rid win] [--capabilities "a,b"]`.
  It fetches the release **unauthenticated**, hashes what it downloads, unpacks it through the
  host's own `IPluginPayloadReader`, and writes the entry from the manifest inside. A hand-typed
  entry passes `PublishedCatalogTests` — those checks are about shape, not about whether the
  checksum matches the asset — so a wrong hash only surfaces when a host's install fails.
- Everything the tool does over the network is unauthenticated on purpose: the host sends no
  credentials, so a private repo's release reads as 404 to it. Checking with `gh` (or any
  authenticated client) passes happily while every host gets nothing.
- GitHub publishes a `sha256` digest per asset. It is **cross-checked, never copied** — GitHub
  recomputes it from whatever was uploaded, so it attests to transport and not to what anyone
  reviewed. The catalog's hash is the one the sync run computed.
- A release zip holds `manifest.json` at its root (or in one wrapping folder), the entry assembly,
  and its `.deps.json` — `AssemblyDependencyResolver` reads that to find plugin-private
  dependencies. Ship no `.pdb`, and no copy of the contract assemblies (see **The published contracts**).
- `Rid` is blank for a build that runs anywhere, which is what a plugin should aim for. Name a
  platform only where an OS API forces a separate build — the Spotify provider's WinRT path is the
  case it exists for. Selection takes version first and platform second.
- `LatestCompatible()` returning null has three causes, and the page must not conflate all of
  them: the wrong plugin API and no build for this platform are both "Not compatible" (the
  tooltip says which, via `HasReleaseForThisHost()` / `HasReleaseForThisPlatform()`), while a
  release published without an https URL and a checksum is "Not verifiable" — that plugin would
  run here, and only its publisher can fix it.
- The catalog is fetched only when the Available tab opens, never at startup — a console runs on
  whatever wifi the room has. A failed fetch keeps the cached copy and shows the error beside it;
  an unknown `schemaVersion` rejects the whole document rather than guessing at the fields that
  carry the checksum.

## What is playing between singers

The break music card names the break music in a corner of the screen. There is no service for it:
it is presentation, so the display provider applies these rules itself, reading
`IBreakMusicService.State` and `CurrentTrack` and the venue's settings, and sends the card whole
on every change the same way the marquee is (`LocalScreenDisplayProvider` for the local screen).

- **It says what is *playing*, not what is cued.** A host's pause and the hand-off to a singer both
  take it down, so the screen never names a track over somebody else's performance. `Suspended` counts as not playing.
- Off for a venue that has never been asked, so the missing key reads as off and it needed no
  backfill.
- A provider that reports no title gets no card; one that reports no artist just loses the second
  line. On macOS a Spotify advert arrives as a track with no artist and an em dash for a title, and
  is drawn as one — recognising ads would mean reading the track id, which nothing does yet.

## Streaming a song

`HlsMediaStreamService` encodes at play time, one ffmpeg per song, into
`<temp>/khost-streams`. There is no pre-render and no stream-copy path: the copy only ever paid off
against a render the host had already made, and making those renders cost more than it saved.

- **Graphics-only sources get `-r 30`.** A CDG has no frame rate of its own, so ffmpeg picks one off
  the first packets and the segment durations then drift from the wall clock.
- **Keyframes are forced on time, not on a frame count.** `-g` is in frames and matches the segment
  length at exactly one source frame rate, and the muxer can only cut where a keyframe already is,
  so `-force_key_frames expr:gte(t,n_forced*<segment>)` with `-sc_threshold 0` is what keeps the
  segments on the clock.
- **Its settings are read live through `IOptionsMonitor`**, never snapshotted in a constructor; only
  the working directory is resolved once, since moving it would strand the sessions already under
  it. App Settings says they apply immediately, and that has to be true.
- `BuildArguments` is static and is where every codec, filter and muxer decision lives, so the unit
  tests can assert the command line without running ffmpeg.

## Components

- Component logic lives in a code-behind partial (`Foo.razor.cs`, `public partial class Foo`), never an inline `@code` block. `@inject` becomes an `[Inject]` property; `@implements` becomes an interface on the partial. `@page`, `@using`, `@inherits`, `@layout`, `@attribute` stay in the `.razor`.
- Never give the code-behind partial a base class — the generated razor partial already supplies one; a second base clause won't compile.
- `_Imports.razor` does not reach `.razor.cs` — code-behind needs its own `using` directives.
- `Dialog` renders its footer only when one is supplied. A viewer — one whose actions commit as they are clicked — supplies none and closes from the header X; a footer button that only closes is furniture.
- Keyboard shortcuts split two ways. A list's arrow keys are a Blazor `@onkeydown` on a focusable element *inside* the panel (`tabindex` + `data-kh-keylist`): keydown fires on the focused element and bubbles up, so a handler on the column around the panel never sees it. Global chords live in `shortcuts.js` and focus `[data-kh-shortcut]`, matched in JS so ordinary typing never crosses the circuit. Both lists share `ListKeyboardShortcuts.Resolve`. A new shortcut has to reach `KeyboardShortcuts.All` as well — the dialog off the menu is the only place a host can discover one.
- Both queues reorder by dragging the row itself through `khSortable` (`sortable-interop.js`), keyed per list — it held one instance, so two sortable lists on screen had each init tear the other down. Three things it has to keep doing: revert the DOM to its pre-drag order before telling .NET (Blazor diffs against its own tree and SortableJS moved nodes behind it), filter the row's `button`s so a press on play or remove is not a drag, and keep `preventOnFilter: false` or Sortable swallows those buttons' clicks along with the drag. Row numbers come from a CSS counter, so a reorder renumbers without a re-render.
- A boolean is a checkbox — `<input type="checkbox" class="kh-form-check-input">` inside a `label.kh-form-check`, with a `span.kh-form-check-label` for its wording. There is no slider and no `.kh-checkbox`. One control, one class.
- `ComboBox<TItem>` is the type-to-search replacement for a native select. It binds the chosen item (not a key), takes every row from a `Search` delegate, and labels runs via `GroupName` without reordering them — the caller groups by sorting. Bind `Text` when the field must also accept a value the list does not contain.

- The console says **song**; the media manager and the importer say **media**. A host puts on songs, and those two pages handle files, formats and paths. `Media` stays the name of the row in code either way.

## CSS/SCSS

- No inline styles or `<style>` elements. BEM with `kh-` prefix (`kh-button--danger`). SCSS nesting. Bootstrap Icons only — no Bootstrap CSS/JS; its utility classes (`d-flex`, `mb-3`, …) resolve to nothing.
- Component styles live beside the component (`Foo.razor.scss` → scoped `Foo.razor.css`; that output is gitignored — never edit or commit it). Shared blocks stay under `wwwroot/scss` via `app.scss`; a partial co-locates only once exactly one component uses its block. Only `app.scss` and `themes/*` may lack a `_` prefix — any other `wwwroot/scss` file without one compiles to its own stylesheet.
- Scoped CSS reaches only elements the component itself renders. A class handed to another component (`<Icon Class="..." />`, `<InputNumber class="..." />`, RenderFragment content) lands on markup carrying a different scope id or none, and the rule silently matches nothing. Reach it with `::deep` under an ancestor this component does render, naming the child class in full: `.kh-foo__row { ::deep .kh-foo__field { ... } }`. Never `::deep &__x` — `&` expands to the parent and swallows it.
- Narrow layouts key off the right width. Panels answer to `@container` (`kh-queue`, `kh-singer-info`, `kh-media-search`) because `panel-resize.js` writes a pixel width — a panel can be 180px wide at 1440. The header answers to `@media`, having no splitter between it and the viewport. Set thresholds against a panel's measured width at 1440, not a round number.
- The console owns the viewport and never scrolls; every other route scrolls as a document, so the status bar follows the content rather than sitting pinned above it. `MainLayout` puts `--scroll` on `.kh-shell` off `/`, which releases the height caps inside it. Release heights only: `.kh-settings-page`'s `flex` is horizontal — it sits in the row `.kh-app__body` lays out — so zeroing it there collapses every settings card to content width. A settings page that skips the `kh-app__body` > `kh-settings-page` wrapper grows until it paints over the footer.
- An auto margin on the cross axis switches off a flex item's stretch, so `max-width` + `margin-inline: auto` leaves a card at its content width until you also give it `width: 100%`.
- A flex item needs `min-width: 0` as well as `white-space: nowrap` before it will truncate; without it, it pushes its neighbours off the row instead.
- A modifier that turns a filled control into an outline one has to clear the fill as well as the border and text: `.kh-button` sets a `--kh-primary` gradient, so overriding only the two left `--outline-danger` painting a solid primary background under red text. Nor is `--kh-primary` a safe stand-in for "active" — a theme may make it a neutral (famicom's is the console's grey plastic), so a state carried by hue alone stops reading. This is why every toggle is a checkbox: `.kh-form-check-input` fills with `--kh-primary` but says "on" with a check glyph, which survives a theme whose brand colour is grey. Use `--kh-danger-bright` rather than `--kh-danger-text-subtle` for danger text, which these dark themes define for exactly that.
- A `.kh-note` explains **one control, and sits directly under it** — never after a run of rows
  carrying a fact about each. In a label-beside-control row it goes **inside the label's own
  column** (`<span class="kh-venue-settings__labelled">`, or inside the `.kh-form-check-label` for
  a checkbox): as the row's next sibling it lands a control's height below the words it explains,
  which reads as a paragraph between rows rather than as part of the field. A note with no label
  column to join — under a stacked `&__field`, or about a whole section — stays a sibling `<p>`,
  and the row above gives up its bottom margin (`&__row:has(+ .kh-note)`). `.kh-note` and
  `.kh-app-settings__apply` share their type: `display: block`, `0.75rem` at `1.5`, in
  `--kh-text-muted`, with the space below in `rem` not `em` so it does not shrink with the smaller
  text. There is deliberately no quieter variant: `--kh-text-muted` is the bottom rung of the
  ladder, so every colour left to pick is brighter.
- `.kh-card__body` pads a direct `<form>` child and nothing else — a card body without a form needs its own padding. A `<select>` needs `kh-form-select`, not `kh-form-control`, or WebKit draws the native macOS pop-up and discards the styling (correct in a browser, wrong only in the Photino window).

## Importing media

The scanner takes everything the host can play, not only karaoke: a venue's break music, its ad
clips and the card it puts up between singers are ordinary library rows.

- **A `.cdg` with no audio beside it is an invalid state, not a quiet song.** The graphics carry
  the words and nothing else. Such a file used to import as a normal row and reach the room as a
  silent stream with a warning in a log nobody reads, so it is now refused twice: the importer
  skips it and counts it failed, and `CompactDiscPlusGraphicsRenderer` fails the play with
  `KH-CDG-NO-AUDIO`. Rows already in a library predate the first check and are caught by the
  second.
  - **`MediaFormats.FindKaraokeAudio` is the one rule**, and every asker goes through it. There
    used to be four copies and two answers: `IsKaraokeTrack` counted *any* audio file beside a
    `.cdg` as the pair's other half, while the probe, the companion resolver and the player looked
    only for `.mp3` — so a `.cdg` next to a `.wav` was excluded from import as "part of a pair" and
    then played silent. It now matches any audio extension, and **without regard to case**: a
    case-sensitive filesystem has `SONG.CDG` and `song.mp3` as a pair that no exact-case lookup on
    a built name ever finds.
  - **`CdgMediaProbe` describes the pair**, reading the audio half for duration and tags, because a
    `.cdg` has neither. That was a special case inlined in `MediaFileParsingService`, which knows
    about no other format and should not have known about this one. It answers **empty, not null**
    when the audio is missing — "I looked and there is nothing", which is what lets the importer
    tell that apart from "I could not tell".
- **`MediaFormats` owns the extension lists and the question.** `TypeForFile(path, videoIsKaraoke)`
  decides what a file is, and the scanner, the row icon and the import itself all ask it — so none
  of them can disagree with the other two.
- Asked of the **path**, not the extension: an `.mp3` with a `.cdg` beside it is the audio half of a
  karaoke pair, an instrumental with no singer on it, and does not belong in break music.
- **A picture track is the one thing no file can settle** — a karaoke video and an ad clip are the
  same formats. The host says which a folder is, on the Import button, and a folder that is not all
  one thing is answered row by row through the Type column. Those answers go in
  `IMediaImportService.TypeOverrides`, keyed by path, and beat anything worked out from the name.
  Switching the batch answer clears them: they were disagreements with an answer that no longer
  applies.

## Tests

xUnit + NSubstitute, and bunit for components. A test that needs anything outside the process — an external binary (ffmpeg), a live service (a network device) — belongs in `KHost.IntegrationTests`; `KHost.UnitTests` must stay skip-free so green means everything ran. In-process I/O (temp files, in-memory SQLite) stays in unit tests.

`MethodUnderTest_Scenario_ExpectedBehavior`; substitutes in field initializers; mirror the source layout (`Domain/Services/Foo.cs` → `Domain/Services/FooTests.cs`). Test an announcement by subscribing a counter to the real broker the service was built with (`using var subscription = _broker.Subscribe<VenuesChanged>(_ => raised++)`), or substitute `IMessageBroker` and assert `Received(1).Announce(...)`. A bunit fixture must register a broker — components `[Inject]` one — or every render throws on the missing service.

**A service that starts work in its constructor will race a test that arranges a substitute after
building it.** Where nothing awaits that work, a substitute stubbed afterwards can have its first
call consumed by the constructor's pass instead of by the test, and an exact call count then fails.
Arrange everything before the service is built, or give the fixture a way to settle the
constructor's work first. **Run the suite under load before trusting a green one** — it is what
found this, and two real defects an idle machine never showed.

Times are stored UTC. **Arrange test data in UTC**: a test that uses the same local clock as the
code cancels the offset and passes, and can only fail on a machine not already at UTC. Local time
belongs in exactly two places — a picker's own model, and comparing a converted local date against
a local today.

A component test renders the component (`BunitContext`, not the obsolete `TestContext`) and dispatches a real event — a handler that exists but is attached to nothing passes every test that calls it directly, which is how the queue's arrow keys sat dead behind tooltips advertising them. Set `JSInterop.Mode = JSRuntimeMode.Loose` (panels call into JS on first render) and give every `Task<List<T>>` substitute a return value: NSubstitute hands back a completed task wrapping `null`, and the component `.Count()`s it.
