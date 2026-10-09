# AGENTS.md

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Photino screen app. Solution: `KHost.slnx` (no `.sln`).

Projects (`src/`):
- `Abstractions` — every interface, the shared models, what a plugin builds against. MIT, no project refs, the bottom layer.
- `Common` — helpers over those contracts. MIT.
- `Domain` (services) / `DataAccess` (EF Core 10 + SQLite).
- `UserInterface` (Blazor Server) and `LocalScreen` (Photino video output).
- `IPC.SignalR` (UI↔Screen), `Secrets` (per-OS secret store behind `ISecretStore`; `Interop/` is ported from Git Credential Manager — keep it textually close to upstream), `LrcLib`, `Telemetry`, `ServiceDefaults`/`AppHost` (Aspire).
- `tools/`: `KHost.CatalogSync`, the CLI that writes entries into the plugin catalog (`plugins.json` in `riddlemd/KHost.Releases`).
- `build/`: `KHost.Analyzers`, a netstandard2.0 Roslyn analyzer referenced only at build time.
- `tests/`: `KHost.UnitTests` — hermetic, no skips. `KHost.IntegrationTests` — needs ffmpeg/ffprobe; the OS secret-store tests skip on any other platform, and each hardware video encoder's tests skip on a machine that cannot run that encoder.

## Commands

```bash
dotnet run --project src/KHost.UserInterface                # run the app (native Photino window)
dotnet run --project src/KHost.UserInterface -- --headless  # no window; console served at http://localhost:5251
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"    # build (redirected so VS's bin/ isn't locked)
dotnet test tests/KHost.UnitTests                           # --filter "FullyQualifiedName~Name" to narrow
dotnet test tests/KHost.IntegrationTests                    # drives real ffmpeg; fails without it (KHOST_SKIP_ENVIRONMENT_TESTS=1 to accept)

dotnet run --project tools/KHost.CatalogSync -- <owner/repo> # add a plugin's GitHub release to KHost.Releases/plugins.json

./build/pack-contracts.sh                                   # pack Abstractions + Common to the local NuGet feed
./build/check-secrets-drift.sh                               # diff src/KHost.Secrets/Interop against the pinned upstream commit; needs network
```

- **Test with `--headless`.** The console is an ordinary page at `http://localhost:5251`: drive it with browser tooling and read the DOM. The Photino window reaches neither, and a LocalScreen window launched over it turns every later capture into a black rectangle.
- Use the windowed run only for the window itself: native chrome, `SetSize`, the appliance lockdown.
- Port 5251 is held by an exclusive `.instance.lock` — stop one run before starting the other.
- SCSS compiles inside `dotnet build` (AspNetCore.SassCompiler); no separate sass step.
- The build needs `node_modules` (`npm install`) for `copy:vendors`.

## Rules

- Interfaces in `src/KHost.Abstractions` (`Services/`, `Repositories/`, `Models/`); implementations in `src/KHost.Domain` or `src/KHost.DataAccess`.
- An interface a plugin must *not* reach sits with its implementation, not in `Abstractions` — e.g. `IQrCodeService` takes an owner id, so a plugin could register over another's QR code.
- Register in the project's `ProjectExtensions` (`AddDomain()` / `AddDataAccess()`); UI-only services in `Startup/ServiceCollectionExtensions.cs`.
- All domain services are singletons — guard mutable state with `SemaphoreSlim`.
- A helper both host and plugin would want goes in `KHost.Common` (MIT, so a plugin can use it without taking PolyForm code), not `Abstractions`.
  - `Common` holds helpers *over* the contracts: string folding aids, formatting, list surgery, the shared drop-position mechanic.
  - A contract, a model, or anything `Abstractions` itself needs goes in `Abstractions`, which references nothing and does not compute (see **No static methods in Abstractions**).
  - Group by area under `Common` (`Media/`, `Plugins/`, `Discovery/`), never its root; mirror that in the tests.
  - Name methods for what the call site reads without repo context: `StreamRate.FromTempo(t)`, `AudioLevels.ClampVolume(v)` (not `For`, `Clamp`), `PluginRid.MatchesThisHost`, `int.CentsToCurrencyString()`. Exception: a member filling a BCL gap keeps the familiar name (`IList<T>.FindIndex`).
- No "gate" services: a guard lives on the service that owns the call (enqueue rules in `PerformanceService.CreateAndEnqueueAsync`, not an `IEnqueueGuard`). `IMediaGateService`/`IMediaProbeService` are routers, not guards: they answer which plugin owns a file; the rule lives in the plugin.
- New repositories/services copy an existing one's shape: repositories extend `BaseRepository<T>` and implement `SortColumns` / `ApplySearchFilters`; services extend `BaseService` (or `BaseRepositoryService<,>` for CRUD).
- In repositories, `using var context = await ContextFactory.CreateDbContextAsync();` per operation — never store a context.
- Services announce, they do not raise events. There is no `StateChanged` and no `IKHostService`. A service takes `IMessageBroker` in its own constructor (never through `BaseService`, which carries only `ILogger`) and calls `Broker.Announce(new ThingChanged())`. Messages are empty records in `KHost.Abstractions.Messaging.Messages`, one per service, named for the fact. See **Messaging**.
- Member order: fields → events → properties → public → protected → private → nested types.
- Every `Task`/`ValueTask`-returning method ends in `Async` — enforced by `AsyncNamingConventionTests`; a new project must be a `ProjectReference` of `KHost.UnitTests` to be covered.
- Names crossing a string boundary (`[JSInvokable]` called from JS, SignalR hub methods invoked by name) break silently on rename: pass `nameof(...)` from C# and take it as a parameter in JS (`SingerQueuePanel` / `sortable-interop.js`, `ScreenClient` / `ScreenHub`).
- Library/users/groups/venues persist in SQL; the queue and the selected venue id are in the JSON cache (`ICacheService`, `./cache/`).
- Dialogs go through `IInteractionDispatcher`, which resolves `IInteractionHandler<TReq, TRes>` from DI; handlers bridge dialogs into awaitable calls with `TaskCompletionSource` and are registered in `Startup/ServiceCollectionExtensions.cs`.
- Licences: `KHost.Abstractions` and `KHost.Common` are MIT; everything else PolyForm Shield (`LICENSE`, plus each MIT project's own `LICENSE`).
  - `LicenceBoundaryTests` enforces: an MIT project references only MIT projects, declares `PackageLicenseExpression`, ships a `LICENSE`.
  - The compiler catches only the circular case — a reference to a leaf like `KHost.LrcLib` builds fine and silently breaks the licence.
- `KHostException` lives in `Abstractions`: it is the only way a plugin reports a failure the host can act on.
- Do NOT commit unless explicitly asked.

## No static methods in Abstractions

- `KHost.Abstractions` holds data and contracts only; a static method is behaviour and belongs in `KHost.Common`.
- Enforced at build time: `build/KHost.Analyzers` raises **KH0001** at the declaration, made fatal in that one project by the `.editorconfig` beside its `.csproj`.
- The analyzer ships `DiagnosticSeverity.Hidden`; the `.editorconfig` line is what makes it an error. `NoStaticMethodsInAbstractionsTests` checks both halves — losing either disables the rule with a green build.
- The reference is `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` (build-time only, no Roslyn assembly in what a plugin redistributes). `LicenceBoundaryTests` skips analyzer references for that reason.
- Exempt (language requires `static`): `Main`, operators and conversions, static constructors, `[ModuleInitializer]`, extension methods.
- Static *fields* and *properties* are untouched — `ScreenCapabilities.None`, `MediaSearchOptions.Default`, `PluginRid.Current` stay.
- Escape hatch: `#pragma warning disable KH0001`. Wanting it usually means the member belongs in `Common`.
- Precedents: `Common/Media/`, `Common/Plugins/`, `Common/Authentication/`, `Common/Repositories/`.

## Finding things on the network

- **A .NET process on macOS cannot send multicast.** A send to `224.0.0.251` fails with `EHOSTUNREACH` ("No route to host") while unicast to the same host succeeds — that is macOS's local-network denial. A command-line binary is denied silently; `dotnet` never appears in Privacy & Security → Local Network.
- So every managed mDNS stack is blind on macOS without saying so: Zeroconf returns an empty list in half a second from a five-second scan.
- Use `Common/Discovery/BonjourBrowser` on macOS: the system daemon over XPC, which the denial does not touch. BCL plus a `DllImport` of `libSystem` — `Common` keeps no package dependencies.
- `BonjourBrowser.IsSupported` is **macOS only**. Elsewhere use a managed stack; the Cast plugin keeps Zeroconf for Windows and Linux and branches on this.
- Do not use Zeroconf's own Bonjour browser: it exists only in its `ios`/`maccatalyst` targets, forcing an Apple TFM, a platform-specific plugin build and a second catalog release. `Rid` stays blank.
- A sweep that finds nothing must **say so** in the log; "blocked", "empty room" and "working fine" otherwise look identical.

## Messaging

`IMessageBroker` (`KHost.Abstractions/Messaging/`) is how services, components and plugins hear about each other.

- **`Announce(message)`**: fire-and-forget ("this moved, redraw"). **`await PublishAsync(message)`**: waits for every handler; use when the publisher's next decision depends on the outcome.
- Handlers run **one at a time, in subscription order**. A handler that throws is logged and skipped.
- Routing is on the message's **runtime type**, exact — a base-type handler is not called for a derived message.
- Subscriptions return `IDisposable`. Hold them in a `SubscriptionSet` and dispose it; a missed unsubscribe keeps a Blazor component and its whole circuit alive.
- Never take a lock around a publish. `ScreenServerService` raises `ScreenConnected`/`ScreenDisconnected` only after releasing its lock. Its handlers are `_ = Task.Run(...)` because the event is a plain `EventHandler` on the hub's thread (awaiting there would be `async void`).
- Components `[Inject] IMessageBroker Broker`, subscribe in `OnInitialized`, dispose the set in `Dispose`.
- These stay plain C# events — do not convert them:
  - LocalScreen's `IMediaPlayer` and `IScreenClient` (separate process; the broker is in-process, SignalR is the transport).
  - `IDialogService.ShowRequested` (a request with a payload and one subscriber).
  - `IPlaybackService.PositionChanged` (twice a second all night; only says `Position` moved — use it to redraw a playhead, nothing else).
  - `IPlaybackService.PerformanceEnded` (a request whose payload the subscriber fills).

## Vocabulary

- **Renderer** means only an `IMediaRenderer` — decides what a display gets when a song starts. Its answer is a **rendition**.
- **Encode**: ffmpeg producing a stream. **Remux**: container change, no re-encode. Never "transcode".
- **Burn in** (verb), **burned-in** (adjective): words drawn into video frames. Not "burn-in" as a noun, not "burnt".
- **Mix**: combining stems — the host's mix (ffmpeg) or the screen's mixer (WebAudio). **Paint**: drawing a video frame for an encode. **Compose**: assembling painted frames and audio into one.
- **Draw**: a display presenting something (lyrics overlay, cards, progress bar). "Render" as a verb only for an `IMediaRenderer` producing its rendition, or the Blazor/HTML sense (`OnAfterRender`, "re-render", bunit's `Render<>`) — never a display drawing.

## What a plugin can reach

- A plugin's entry point is built with `ActivatorUtilities.CreateInstance` against the host container; it takes `KHost.Abstractions` service interfaces in its constructor. No facade.
- `IPluginContext` carries the plugin's own manifest and settings, plus the calls where the *host* supplies the identity: its secrets and its QR code.

### Acquisition
- Downloading media for the queue goes through `IMediaAcquisitionService`. Its rules, which nothing else may re-implement:
  - an import is idempotent by `FilePath`;
  - the media row's status and the `IDownloadsService` entry move together;
  - `DiscardImportAsync` deletes only a row still in `Downloading`.
- Enqueuing is not on it: compose `ISingerQueueService.SelectedUserId` with `IPerformanceService.CreateAndEnqueueAsync`. Folding the pair into either closes a constructor cycle.
- **`Processing` is the second half of `Downloading`, not a separate state.** A provider that must convert the bytes calls `BeginProcessingAsync(mediaId)` once the download is in and verified.
  - It is the only transition a plugin may make besides the three settles; it moves only a `Downloading` row and leaves the `IDownloadsService` entry alone.
  - The entry carries a `DownloadPhase` so the Downloads page names which half a percentage measures; changing phase clears the progress.
  - Ask `MediaStatuses.IsAcquiring()` (`Common/Media/`), never `== MediaStatus.Downloading`, or a phase-two row is stranded. `MediaStatuses.Acquiring` is the same question as data for EF.
- `FailImportAsync` takes an optional reason the page shows — a line a host can act on, never a stack trace.
- `DiscardImportAsync` reads `FilePath` itself: deletes the row only when nothing is on disk; keeps it as `Broken` when a file outlived the cancel.

### Talking to the host
- **Tell the host something**: inject `IFlashService`, call `Show(text, FlashType)`. For a line the host reads and moves on from (why an action was refused, a lapsed sign-in), never a decision (that is `IInteractionDispatcher`). Name the plugin in the text. Flash only what a host can act on (a failure the host caused, e.g. pressing play) — not background retries; the log takes the rest.
- **Ask the host for a value**: inject `IInteractionDispatcher`, send a `TextPromptRequest`.
  - Nothing from that round trip reaches `plugins.json`; keep collected values via `IPluginContext.SetSecretAsync`.
  - Never keep a raw password — hash it first.
  - `Secret: true` masks input on screen only; it says nothing about storage.
- **Show a list**: send a `ShowPluginTableRequest`. A plugin ships no markup (its assembly never reaches the renderer); it describes a table and the host draws it.
  - Fixed: `Title` and the `PluginTableColumn`s. Everything that changes — rows, buttons above the table, the empty-table line — comes back from **one** `LoadAsync`. Separate delegates let a stopped search show a "Searching" button over an empty table.
  - `PluginTableAction.PerformAsync` is a **delegate, not a key the host dispatches back** (same shape as `MediaProviderAction`). The host keeps no string→behaviour map.
  - The dialog re-reads after every action and whenever **`PluginTableChanged`** is announced. A list that fills in on its own (a network sweep) must announce it, or an open table goes stale.
  - Reached from a Plugins-page button, so the plugin also implements `IPluginButtonHandler` on the same extension object. `DescribeButton` lets the row report state without opening anything.
- **Plugins-page row buttons**: declared in the manifest (`PluginButtonDefinition`), implemented by `IPluginButtonHandler`. The host runs `InvokeButtonAsync(key)` then re-reads `DescribeButton(key)`, so a button can relabel, hide or disable itself. Reached by plugin id through `IPluginButtonService`.

### Gating content
- **Gating is the plugin's job.** `IMediaPlaybackGate` exists only so a subscription provider can honour its own terms. The host routes the question to the owning plugin and obeys. Add no host-side enforcement; do not try to make it airtight against a host who owns the machine.
- `CanAsync(MediaAction, Media)` takes `Queue`, `Render` or `Play`; a provider whose answer never varies ignores it.
  - Call sites: `PerformanceService.CreateAndEnqueueAsync` (Queue), `PlaybackService.LoadAsync` (Play).
  - `Render` is raised by nothing; keep it in the enum (plugins compile against it; the native render path will need it).
  - `Queue` and `Play` may raise a sign-in first.
- **Ownership is asked by name first.** `IMediaGateService` asks every gate's `Claims(path)` before reading any tag. Only when nothing claims the path does it read the container tag `IMediaPlaybackGate.MetadataTag` (`khost_provider`), naming one gate to ask.
  - The tag read is skipped for a file not yet on disk (the gate is asked at enqueue, while a download may still be arriving).
  - A provider whose container extension nothing else produces should gate on the extension alone.
  - `Claims` is false by default and must answer from the **path alone**.
  - Writing the tag onto a render needs ffmpeg's `+use_metadata_tags` movflag.
- The gate is on the **source container**, not the render (a temp file the library never points at). A render's odd extension is obscurity, not protection — KHost reads media by content.
- A block refuses the action like a non-Ready row and flashes the gate's reason. A gate that **throws** is logged and the action proceeds. The check runs on every load: keep it in-memory unless the content is worth a round trip.

### Probing a plugin's own container
- A plugin that ships its own container describes it through `IMediaProbe`; ffprobe reports an unreadable container as "Invalid data found", indistinguishable from an empty file.
- `CanProbe(path)` claims from the path alone. `ProbeAsync` returns a `MediaProbeResult` (duration, audio tracks, container tags). **Null ≠ empty**: null is "could not tell", empty is "looked, nothing there".
- The probe returns facts; the asking service keeps policy (`AudioTrackService` decides one track is nothing to balance, and a set with no music track is not worth offering). Tracks come back already roled.
- `IMediaProbeService` routes to the first plugin that claims the file, else `FfprobeMediaProbe`, registered **keyed** (`MediaProbeService.FallbackKey`) so it never appears in the `IMediaProbe` enumerable (it claims every file).
- A plugin that throws while deciding is skipped; one that throws reading its own format answers null, no fall-through.
- Nothing is cached: a file swapped under an unchanged path must be re-read.
- **`IMediaPlaybackGate.Claims` asks who *owns* a file; `IMediaProbe.CanProbe` asks who can *read* it.** Answer each for what it asks — a plugin gating content the host reads fine claims the gate, not the probe.
- Both must answer from the **path alone**: each runs for every queued turn on every reconcile; opening the file turns a bulk enqueue into thousands of reads.
- `Claims`'s **default body** is behaviour in `Abstractions` that KH0001 cannot see — a deliberate exception so a plugin with no opinion loads unchanged, not a precedent.

### Rendering
- **There is no host-side *pre*-render.** A format the host cannot play is **unplayable** until the native render path lands — see `src/KHost.LocalScreen/RESEARCH.md`. Do not reintroduce `IMediaPreparer` / `PreparedMediaService` / `PerformancePreparation`.
- **`IMediaRenderer` is not a pre-render.** It turns one file into something a display can play, asked **once, when a song starts**. It produces nothing the stream session does not sweep, caches nothing, reports no progress, holds no state past the song.
  - It answers with what to play: a `MediaRendition` carries a URL to play end to end, the separate `Stems` a display mixes itself, or both. `StreamUrl` on `DisplayLoad` is nullable: a stems-only format on a screen that mixes runs **no ffmpeg at all**.
  - **Claim by file, not by extension or `MediaType`**: `CanRender(path)` plus a keyed fallback, same shape as `IMediaProbe`. (`MediaFormats.TypeForFile` does not know a plugin's extensions; a plain `.mp4` is `Karaoke` or `Video` by a caller flag.) Asked once per song, so unlike `Claims`/`CanProbe` it **may open the file**.
  - **Null means "nothing better for this target"** and falls through to `StreamingMediaRenderer`, which claims everything and encodes.
  - **A renderer supplies stems; the host decides what the target needs.** A stems-format renderer answers with its `Stems` for **every** target, key and tempo, and makes no mixing decision.
    - `MediaRendererService` passes stems straight through only to a target that `MixesStems` with no key/tempo change and no `BurnLyrics`.
    - Anything else goes to `StemMixdown`: `HlsMediaStreamService.OpenStemsAsync` reads every stem as an input (off disk when it sits in a host session) through the same per-voice mix graph, key/tempo chain and burn-in overlay as any encode, over black. The encode **adopts** the renderer's session, so closing it sweeps the stems.
    - Such a plugin needs no `IPlayableMediaSource` and should not implement one; that contract is for a format the host's encoder cannot open.
    - A rendition carrying its own `Url` is never re-encoded.
  - `RenderTarget.MixesStems` says whether the one connected display mixes for itself; a device hearing the host's mix needs the encode.
  - Everything streams through `HlsMediaStreamService` unless a renderer says otherwise.
- **A renderer may inherit the encode.** `CompactDiscPlusGraphicsRenderer` claims `.cdg` and derives from `StreamingMediaRenderer` (subcode graphics need ffmpeg). A renderer owns whether media is *valid*; `BuildArguments` owns how it is *encoded*. A `.cdg` with no audio beside it fails with `KH-CDG-NO-AUDIO` (see **Importing media**).

### Burn-in
- **The host burns in the words, not the format's renderer.** When the target asks for `RenderTarget.BurnLyrics` and `ITimedLyricsService` has timed words, `StreamingMediaRenderer` opens the song through `LyricBurnIn`.
- Words are painted by `TimedLyricsPainter` (SkiaSharp + HarfBuzz, `Domain/Services/BurnIn/`) and fed down a raw RGBA pipe into the **same** ffmpeg run as an overlay input, so key, tempo and per-voice mix still apply. Every provider supplying `TimedLyrics` gets this, as a file or as stems. No timed words → plain encode.
- **One set of drawing rules: the painter follows `screen-ui/lyrics-overlay.js`. Change one, change the other.**
  - Page fitted and centred; theme colours for anything the timing leaves unset; linear wipe.
  - Count-ins ease over one step and are gone by the next page's arrival.
  - A line-start lead-in runs to the line's leading edge.
  - A lead-in for a syllable part way along the line (`ArriveAtSyllable`) is three dots over that syllable's ink, going out one per third from `StartSeconds` to when it lights.
  - A line with no position stacks under the one before.
  - Where they differ, the painter is right: it shapes through HarfBuzz, so joined scripts join and a right-to-left line is laid from its box's right edge.
- **The picture under the words**: the source's own video if it has one (fitted into 1280x720), else black — never the venue's card. There is no song-background picker or setting.
  - `SongBackdrops.ForPlaying` is that rule, asked by the burn-in; a visualiser goes there in place of black.
  - **A timed-lyric song never takes a picture from an audio source** (an extension in `MediaFormats.AudioExtensions`), and a cover image (`attached_pic` stream) is never a picture in any file.
  - This holds on every encode: for a display that draws its own words, `StreamingMediaRenderer` opens a timed-lyric song through `IBurnInStreamService.OpenUnderDrawnWordsAsync`, mapping `0:V` (no attached pictures) from a video and no picture from an audio file — otherwise ffmpeg turns an MP3's cover into a one-frame video.
  - A song with no timed words still shows its cover.
  - No darkened band behind the words on either drawer; the outline keeps them legible.
- **Fonts are the system's**, in a web view's `sans-serif` order per OS: Helvetica, Arial, DejaVu Sans, Liberation Sans, Noto Sans, then Skia's default; per-line fallback through the OS for missing characters. Nothing bundled.
- `CompactDiscPlusGraphicsRenderer` passes no burn-in: a CDG's words are its picture.

### Displays
- **`IDisplayProvider` is a transport: transport and control, nothing drawn.** It finds destinations, connects to one, hands it what to play, drives transport. It is told what the show is.
- **The screens provider is core, not a plugin.** The host registers it; it travels the same path as a plugin display. `PluginLoader` must not bind it; it never appears on the Plugins page. Chromecast is the plugin-supplied one.
- **One display, full stop** — the local screen *or* a receiver, never both, never two. No multi-screen or multi-device seam anywhere: `ConnectedDeviceId`, `SessionId` and every argument-free member address the one device.
  - A provider refuses a connection to a different device rather than replacing its own (`LocalScreenDisplayProvider.ConnectAsync`).
  - Picking a display disconnects every other provider first (`SettingsButton.SelectDisplayAsync`, and the "Launch Screen" confirm in `DialogService`).
  - `PlaybackService` and the break music provider use `ConnectedDisplay.Find` (several providers are registered at once).
- **No drawing members.** A plugin writes discovery, connection, `LoadAsync(DisplayLoad)`, play, pause, stop, seek. `DescribeTarget` (`RenderTarget.None`), `SetStemVolumeAsync` (false), `SetVolumeAsync` and the second channel have default bodies (same exception as `IMediaPlaybackGate.Claims`). Arguments are `Abstractions` models (`DisplayLoad`, `StemLevel`, `BackgroundLoad`), never the screen's wire commands.
- `DisplayDevice` carries only `SupportsAudio`, `SupportsVideo`, `SupportsFade`. What a device takes is the provider's answer. A display that cannot draw lyrics asks for `RenderTarget.BurnLyrics`; anything else it cannot draw, it does not draw.
- **`SetStemVolumeAsync` answers whether the level landed.** False → the host rebuilds the stream at the playhead with the new mix baked in. Called only while the loaded song carries stems.
- **The wire to the local screen is not a contract.** `IScreenServer`, `IScreenClient`, `IScreenProvider`, `IScreenKeyStore` and every command/state type live in `KHost.IPC.SignalR.Contracts` (referenced by `Domain`); nothing a plugin builds against names them. `LocalScreenDisplayProvider` maps host calls onto its commands.
- **The provider owns presentation; the host supplies data and services.** `LocalScreenDisplayProvider` hears what moved, pulls the whole current state of what that message drives, and decides the look, reading **only what a plugin display can read**:
  - `IPlaybackService.CurrentProgram` (idle, a song, or an ad still; announced by `PlaybackChanged` — compare by value), `IUpNextService` + `UpNextChanged`, `IQrCodeOfferService` + `QrCodeOfferChanged`, `NextSingerAnnounced`, `IBreakMusicService`, `ITimedLyricsService`, the venue's settings.
  - It composes the marquee itself (`BuildMarqueeAsync`); wording rules live in `Common/Display/MarqueeText` for plugin displays too.
  - QR SVG encoding, filling an unset placement and building screen commands stay inside it. Registering a code (`IQrCodeService`, takes an owner id) stays Domain-only.
  - `IPlaybackService` takes every display, so a provider resolves it on first use, never in its constructor.
- **`UpNextChanged` is announced only by `UpNextService`.** It hears `SingerQueueChanged`, `PerformancesChanged`, `PlaybackChanged` (only when the singer at the mic moved) and `SelectedVenueChanged` (only when `AllowAliases` moved), and settles for 50ms so a stop is one announcement. Producers never announce it. The marquee names exactly the singers `IUpNextService` reads.
- **`DescribeTarget()`**: `PlaybackService` asks the connected provider on every load; a throw or null is `RenderTarget.None` (one mixed stream, no burned-in words, also the default body). `RenderTarget.BurnLyrics` is honoured by the host's encode for any song with timed words; a renderer with its own rendition may honour it or return its normal rendition. The local screen overrides it: stems, no burned-in words.
- **An ad still's `ImageUrl`** uses the stream URL's base address under `/media`, which answers off-box. It may name loopback; a provider for a networked device swaps in a LAN address as for `StreamUrl`.
- **`SupportsFade` is the one capability the host acts on.** `StopAsync` waits out the fade it asks for; a display that cannot fade (a receiver) stops instantly, as does a paused stop. A device not yet listed is taken to fade (over-waiting is harmless; under-waiting cuts a word).
- **A screen is local only** (launched by the host on its own machine). `discovery` means a network sweep for Cast and "open one" for screens. **`SearchesForDevices`** says which, and the console uses it: a "search for devices" header must not launch a screen; one with only screens behind it must not offer a search. True by default.
- **No roles and no sync.** No audio screen, primary, timeline, or per-screen audio/video override.
  - The connected display defines the song's clock: every provider raises `PlaybackStatusChanged` with its own timestamped position; the host trusts only the connected one. A screen's reports reach `PlaybackService` through `LocalScreenDisplayProvider`, not a side channel. Nothing is corrected towards anything.
  - No venue volume: `LocalScreenDisplayProvider` sends the screen full level on each connect; break music providers play at their own level. `IBreakMusicProvider.SetVolumeAsync` stays on the contract; the host never calls it.
- **Covering a rebuild is the transport's job.** Changing key, tempo or mix reopens the stream at the playhead; the host resumes there and **skips nothing** (skipping forward lands on unwritten data and starves the element). A screen keeps its old element playing until the new one has sound — a host seek defeats that. A transport without such a mechanism compensates inside its own `LoadAsync`.
- **The server registers one screen — a constant, not an option.** A second screen is refused; a re-registration under the same id replaces the first. Log every refusal in `TryRegisterScreen` (a refused screen shows "Lost the host", indistinguishable from a crash). `IScreenServer` sends only via `BroadcastCommandAsync`, signed under the screen's own key.
- The off-box HTTP surface — `LanAccessPolicy.IsMachineFacing`, the permissive CORS header, ranged GETs — stays host surface; do not move it behind the Cast provider.

### QR codes, import formats, extension binding, `bin/`
- **QR codes**: the manifest's `qrCode` is the standing registration, read without resolving the plugin. `IPluginContext.RegisterQrCodeAsync` is the live one. The venue names **one** source in `Venue.Settings.QrCodeSource`, **none by default**; other owners' codes are held, not drawn. The owner is stamped from the loaded manifest, never passed by the caller. Placement is the venue's alone.
- **Import formats**: manifest `importFormats: [".ext"]`, read from `IPluginRegistry` without resolving the plugin, unioned with the built-ins for **loaded** plugins only, normalised to leading-dot lowercase. A *filter* only — says a row may be made, not that the host can play it. The folder is never content-probed.
- **A plugin extension type is one singleton across every extension interface it implements** (one object, one session key). The bound interfaces are a hand-written list in `PluginLoader`; omitting one is **silent**. `PluginExtensionInterfaceTests` fails on any Abstractions interface the domain collects that is not listed.
- **The host's `bin/` is shared; a plugin finds it via `IHostDirectories.BinDirectory`.** A plugin's own program goes there under a distinctly own name. Never write, replace or delete `ffmpeg`/`ffprobe`. The host looks there before PATH. A plugin running ffmpeg asks `IFFmpegService.Locate` rather than naming it bare. See **Finding and installing FFmpeg**.

## The published contracts

- `KHost.Abstractions` and `KHost.Common` are **NuGet packages**; a plugin takes a `PackageReference`, never a `ProjectReference` into this repo.
- `<ContractsVersion>` in `Directory.Build.props` versions both. Bump it on any shape change, additions included; 0.x while the contracts move.
- A manifest's `apiVersion` is the plugin API it was **built against**. The host runs, installs and offers it only when `PluginApi.MinimumVersion <= apiVersion <= PluginApi.CurrentVersion` (both at **1**). The rule lives in `Common/Plugins/PluginApiRange` (`ThisHost.Covers`, `DescribeRefusal`); never compare to either constant directly.
  - `CurrentVersion` moves on any **addition** a plugin could call or implement (a new interface, member, model field, enum value), so a plugin built against it is refused by an older host with a reason, not a run-time `MissingMethodException`.
  - `MinimumVersion` moves only on a **break**: changing a method a plugin **calls or implements**, including adding an optional parameter (the default compiles into the call site; a changed implemented signature is a `TypeLoadException` at load), or removing anything. A break moves `CurrentVersion` too.
  - Not a break: a new interface member with a **default body** (still an addition).
  - Contract enums stay **append-only** with explicit values: a plugin compiles them as numbers.
  - Do not reason from the published catalog about who implements what — the hand-installed plugin is the one running.
  - Widening a method silently changes what `Received(1).Foo(id)` asserts in a plugin's tests: assert the argument, not the bare call.
- A plugin excludes their runtime assets: `<PackageReference Include="KHost.Abstractions" ExcludeAssets="runtime" />`. The host has both in its default context and `PluginLoadContext.Load` returns null for them. A plugin's *test* project takes them normally.
- While unreleased, `./build/pack-contracts.sh` packs both into a local folder feed. Register once per machine: `dotnet nuget add source ~/.nuget/khost-local -n khost-local`.
- The script clears the matching global-packages entries before packing: NuGet never re-reads an extracted version, so without it a plugin builds against whatever it first restored.
- The analyzer reference in `KHost.Abstractions` carries `PrivateAssets="all"` so the package declares no dependency on the unpublished `KHost.Analyzers`.

## Plugin catalog and installs

- The Plugins page's Available tab installs from `plugins.json` in the public repo **`riddlemd/KHost.Releases`** (served raw from `main`; `PluginCatalog:Url`). Format is `PluginCatalog` (`schemaVersion` 1); that repo's CI checks it — this repo holds no catalog and no catalog test.
- The catalog is the **trust root** (a plugin runs in-process with host access). A release is offered only over https with a `sha256`. The download is hashed; every zip entry is checked for escapes *before* any is written; the manifest must declare the same id and an `apiVersion` in this host's range. `EntryAssembly` must resolve inside the plugin folder (`PluginLoader` passes it straight to `LoadFromAssemblyPath`).
- Presentation metadata (repository, author, capabilities) goes in the catalog, not `PluginManifest` — the manifest is MIT and a new field breaks every external plugin's build (same argument as `MediaSearchEntity`).
- Nothing installs into a running host. `IPluginStagingArea` parks payloads in `plugins-staging/`, a **sibling** of `plugins/` (`PluginLoader.Discover` treats every subdirectory of `plugins/` as a plugin).
  - `<id>/` staged install; `<folder>.remove` pending removal; `<id>.failed/` a payload the last start could not apply, with `error.txt` beside it (stops retries); `.work/` download scratch (inside staging so the final `Directory.Move` never crosses a volume).
  - Installs keyed by id, removals by folder name (two folders may carry one id): an install replaces the plugin wherever it sits; a removal targets one row.
  - A marker naming anything but a direct child of `plugins/` is ignored.
- `ApplyPending()` runs from `AddPlugins` before `Discover`: no DI, no logger, and a failure must never stop startup. It maps id → folders by reading each manifest, so an update replaces a hand-dropped plugin under any folder name and every duplicate copy.
- **Selection** (`LatestCompatibleRelease`): the highest plugin version whose `apiVersion` is in `[MinimumVersion, CurrentVersion]`, installable, for this platform; platform second. Entries are never removed, so every host finds the newest it runs.
- **`tools/KHost.CatalogSync` writes entries** (`-- <owner/repo> [--rid win] [--asset name.zip] [--capabilities "a,b"] [--catalog <path>]`; default catalog `../../KHost.Releases/main/plugins.json`, then `../KHost.Releases/plugins.json`). It downloads **unauthenticated** (a host sends no credentials, so a private repo is a 404), hashes, cross-checks GitHub's digest (never copies it), and unpacks through `IPluginPayloadReader.UnpackForCatalog`: every install check but the API range, since it lists builds for every host. The install path (`Unpack`) keeps the range check. Release and catalog rules for maintainers: KHost.Releases' README and AGENTS.md.
- A release zip holds `manifest.json` at its root (or in one wrapping folder), the entry assembly and its `.deps.json` (read by `AssemblyDependencyResolver`); no `.pdb`, no contract assemblies. `Rid` blank = runs anywhere (the goal); name a platform only when an OS API forces it.
- `LatestCompatible()` null has three causes; do not conflate them. Out of API range or no build for this platform → "Not compatible" (tooltip: `DescribeApiRefusal()` says whether KHost or the plugin needs the update, else the platform). No https URL and checksum → "Not verifiable".
- Fetch the catalog only when the Available tab opens, never at startup. A failed fetch keeps the cached copy and shows the error. An unknown `schemaVersion` rejects the whole document.

## What is playing between singers

- The break music card is presentation, not a service: the display provider applies these rules itself from `IBreakMusicService.State`, `CurrentTrack` and the venue's settings, and sends the card whole on every change, like the marquee (`LocalScreenDisplayProvider` for the local screen).
- **It shows what is *playing*, not what is cued.** A host's pause and the hand-off to a singer take it down. `Suspended` counts as not playing.
- Off for a venue that has never set it (missing key reads as off; no backfill).
- No title → no card. No artist → no second line.
- On macOS a Spotify advert arrives with no artist and an em dash title, and is drawn as such; nothing reads the track id to detect ads.

## The visualiser under the words

- A MilkDrop preset (butterchurn, WebGL 2) drawn by the **local screen** under a song's words in place of black. Nothing else gets one: burned-in words (Cast, any display that cannot draw) stay on black; a song with its own picture keeps it.
- **Rule: `SongBackdrops.ForPlaying` answers `Black`**, and the venue names a visualisation playlist (`VisualisationPlaylistId`, null when unset, no backfill) with at least one entry.
  - `LocalScreenDisplayProvider` decides after every load, on a venue edit, and on `VisualisationPlaylistsChanged` / `VisualiserPresetsChanged`; sends `SetVisualiserCommand` (IPC only, not a contract).
  - A stems load has no picture; a stream from a non-audio file asks `ISourcePictureProbe` once per song.
  - Idle and an ad still send it off; the screen takes it down itself once a stop has faded out.
- **Playlists**: `IVisualisationPlaylistService` (SQL, `VisualisationPlaylists` / `VisualisationEntries`) holds ordered entries; each is a preset plus its own brightness, colour and audio sensitivity (a preset may repeat).
  - `SelectNextAsync` advances in order or shuffles without an immediate repeat; the rotation is in memory and restarts on a restart or entry edit.
  - The provider picks only once a song is known to draw one (a video does not use up a turn), keeps the song's entry by id across a rebuild or rejoin, and re-reads it on an edit. An entry removed mid-song moves to the next.
- **`VisualisationPlaylist.DefaultId`** is the "Default Visualizations" playlist every install ships: every ambient scene, in the page's order.
  - Seeded by a migration, data only — no `HasData` (it would also seed an `EnsureCreated()` test database). `DatabaseInitializer` restores it at startup if missing.
  - `VisualisationPlaylistService.DeleteAsync` refuses it; the page disables its Delete button. Rename, shuffle and entry edits stay open.
  - Every venue-creation path (setup wizard, Add Venue, `EditVenueModel.From(null, …)`) points a new venue at it. Duplicating keeps the source's value. An existing venue's unset `VisualisationPlaylistId` is left alone.
- **Presets are named, never numbered.**
  - Shipped: by name in `screen-ui/visualiser-presets.js`, mirrored by `VisualiserPresetService.BundledNames` (a test holds them together).
  - Imported: a butterchurn `.json`, validated for shape and size (256 KB), kept under `cache/visualiser-presets/<name>.json` (not in the library), served from `/media/visualiser-presets/{name}?v=<write time>`.
  - A preset's equations are code; importing is trusting. `.milk` files are not read. An unresolvable name draws black. A preset named by a song's timing is ignored.
- **Built-ins are host drawings, not presets.** `VisualiserPresetSource.BuiltIn` names a style in `screen-ui/eq-visualisers.js` (spectrum bars, mirrored bars, oscilloscope, twin VU meters) by a stable id mirrored with its title in `VisualiserPresetService.BuiltIns` (a test holds them together); listed first on the page.
  - Canvas 2D on their own canvas (`#visualiser-eq`) — a canvas keeps one context kind for life and butterchurn's is WebGL — so they work without WebGL 2.
  - Per entry: bar count (16/32/64, snapped on save) and palette (classic green-to-red, the screen's `#8558fa` accent, or one `#rrggbb`). A MilkDrop entry ignores both.
  - Fed like butterchurn: a live tap's spectrum, else the host's eight bands mapped onto the bars, linear between band centres on a log scale. Oscilloscope and meters read butterchurn's samples (on host levels, the synthesised tones). Sensitivity's swing gain reaches the bars as a dB shift.
  - All but the mirrored bars sit in the lower part of the screen.
  - **Ambient scenes** are built-ins named `ambient-*` (drifting colour, floating lights, rising embers, pulse rings, sweeping beams): calm full-screen scenes listed under "Ambient"; the prefix is the grouping (`VisualiserPresetService.IsAmbient`). They take the palette (classic = soft colour mix), ignore bar count. Loudness and bass are read off the same bars, eased to rise at most `AMBIENT_RISE` and fall `AMBIENT_FALL` per frame; no shape painted past `AMBIENT_MAX_ALPHA` (no flashing). All motion is frame-counted from a seed, so pause holds the frame and a still is repeatable.
- **A video is an entry too** (`VisualiserPresetSource.Video`, `VisualisationEntry.VideoMediaId`, a soft reference to a library `Video` row): the screen plays it muted and looping in `#visualiser-video` under the words, from 0:00 each song. Not clocked to the song (key, tempo and seek leave it alone), frozen on pause, faded with a stop, never on the next-singer card. Brightness and colour apply; bar count, palette and sensitivity do not, and no levels are read.
  - `IVideoBackdropService` (Domain only: it serves files off this machine) probes the file **every time** and hands out a token URL under `/media/backdrops/{token}`. H.264 in MP4/MOV at `yuv420p`/`yuvj420p` with no sound or AAC is served as it is; anything else (10-bit included: a `<video>` cannot play it) is encoded once to a silent 720p H.264 MP4, capped at 10 minutes, kept for **this run only** by path + write time in `<temp>/khost-backdrops/<guid>` (swept like stream sessions).
  - Always a plain MP4, never HLS: the song stream needs hls.js/MSE and its WebKit opaque-origin workarounds; a background does not.
  - The encode never blocks the load: the provider sends the visualiser off, then the video once the URL lands, only if the same song and pick are still current. A missing or unplayable row draws black.
  - The Visualisations page previews only a video that plays as it is (`DirectUrlForAsync`, never encodes) and says so for the rest.
- **Look**: brightness and colour are a CSS filter on the canvas. Sensitivity scales each frame's swing from its own running loudness (a plain gain cancels out — butterchurn reads every band against its own average), applied after samples are filled, so tap and host levels are treated alike.
- **The Visualisations page preview** runs the screen's own `visualiser.js` and `lyrics-overlay.js`, copied unchanged into `wwwroot/js/visualiser/` by `copy:vendors` (a test fails on drift), in a `sandbox="allow-scripts"` frame with no origin (an imported preset is code; the console holds the host's session), fed a synthetic beat and two sample lines.
- **It listens, never re-routes.** A stem song is tapped off the mixer's master gain (after the fade) into analysers that lead nowhere. An encoded song is tapped through `captureStream()` where available. Never use `createMediaElementSource`: it takes the element off the speakers for good, binds it to one context a sleep can kill, and puts fades behind a second volume.
- **No tap → the host's levels.** WebKit has no `captureStream`, so on macOS an encoded song (including a stem song after a key/tempo change) has nothing to listen to.
  - For every song that draws a visualiser, `LocalScreenDisplayProvider` starts `ISongLevelsService` once (a key change's reload keeps them) and sends the URL in `SetVisualiserCommand.LevelsUrl`; the screen fetches `/media/levels/{token}`, which waits for the read.
  - Inputs (`SongLevels.InputsFor`): the stems at their loaded gains when the load carries stems; else, when the load is the host's mix of a renderer's stems, those stems via `IStemStreamService.StemsMixedInto` from the stream URL; else the file when the host opens the format (a `.cdg` through its audio). Stems are read before key/tempo, so they are in song time. A provider's container with no stems draws without the beat.
  - Format (`SongLevels`): eight bands per channel, 30 frames a second of song time, a quarter-decibel per byte against each band's own loudest. ffmpeg at one thread and below-normal priority. Band edges on butterchurn's 320 Hz and 2800 Hz splits.
  - Drawing: `synthesiseVisualiserLevels` rebuilds a tone per band at the frame the **song clock** (`songClock`, the words' clock) falls in, so seeks, rebuilt-stream offsets and tempo match the words, and a lead-in hold reads as silence. A live tap always wins.
  - One song at a time, in memory, dropped when the next song starts or the screen goes idle — not in the stream session (a key change closes it while the levels stay valid).
- **A preset must stay alive in silence** (several MilkDrop presets fade to black with no input, and levels may be missing). `screen-ui/VISUALISER-NOTICE.md` says how the set was chosen.
- No darkened band behind the words on either drawer: `lyrics-overlay.js` and `TimedLyricsPainter` draw only the words (and chase, count-ins, lead-ins); the black outline keeps them legible.
- Capped at 30fps and a 1280x720 drawing buffer, frozen on pause (last frame holds), drawn only while shown. The engine is built on first use and never with its own audio context: samples are read here and handed to each frame (every stem song brings a new context).

## Streaming a song

- `HlsMediaStreamService` encodes at play time, one ffmpeg per song, into `<temp>/khost-streams`. No pre-render, no stream-copy path.
- **Graphics-only sources get `-r 30`** (a CDG has no frame rate; otherwise segment durations drift from the wall clock).
- **Force keyframes on time, not frame count**: `-force_key_frames expr:gte(t,n_forced*<segment>)` with `-sc_threshold 0`. `-g` is in frames and only matches the segment length at one source frame rate.
- **Settings are read live through `IOptionsMonitor`**, never snapshotted in a constructor (App Settings promises they apply immediately). Only the working directory is resolved once (moving it would strand live sessions).
- `BuildArguments` is static and holds every codec, filter and muxer decision, so unit tests assert the command line without ffmpeg.
- **Video encoder per song: setting, then probe, then libx264.** `MediaStream:Encoder` (`Auto`, `Hardware`, `Software`; App Settings' "Video encoder") is read live. `Software` never probes.
  - Otherwise `IVideoEncoderSelector` (Domain, not Abstractions — a plugin must not reach it) runs each platform candidate through a short real encode and takes the first whose segments land on the clock. `ffmpeg -encoders` lists what a build was compiled with, not what the GPU can do.
  - Cached per ffmpeg path and write time. Probed in the background at startup (`HostInitialization`); a song opening meanwhile waits for that probe. `FFmpegChanged` re-probes once the ffmpeg on disk is a different file (it is announced per install percent too; an unchanged ffmpeg hits the cache).
  - Audio alone names no encoder and always runs software.
- **A failing hardware encoder is caught twice.**
  - No playlist produced → retried on libx264 before the URL is handed out.
  - Dies after hand-out → carried on in libx264 in the **same playlist**: hardware runs start with `omit_endlist`; on non-zero exit the host starts libx264 at the listed output's song time with `append_list` and `-output_ts_offset` (numbering on, timestamps continuous, join marked `#EXT-X-DISCONTINUITY`); a burn-in is re-planned from that time into the new pipe. On a clean exit the host writes `#EXT-X-ENDLIST` itself.
  - The encoder is blamed (`ReportFailure`) only once libx264 succeeds, and is not chosen again until restart.
  - A software run is never carried on.
- **A burn-in is one more input, not another encode.** Painted frames arrive as `-f rawvideo -pix_fmt rgba … -i pipe:0` in straight alpha; one `filter_complex` overlays them and carries the audio mix, so every map is explicit.
  - Picture is the base, words the overlay — `overlay` takes alpha from the base, so reversed, the words vanish while the encode runs.
  - The base is brought to the painted 30fps.
  - All inputs precede the output seek, or `-ss` binds to the pipe.
  - The session owns the painter: closing it cancels painting and kills ffmpeg, releasing a write blocked in the pipe.

## Finding and installing FFmpeg

- **KHost never ships FFmpeg.** `IFFmpegService` (`Domain/Services/FFmpeg/`) finds ffmpeg/ffprobe and, when asked, installs a pinned third-party build.
- **Resolution order: App Settings folder (`FFmpegPath`) → the host's `bin/` → PATH**, first existing file wins, per program. A configured folder lacking the program falls through.
- `Locate` is a file check asked per song, so installs and setting changes apply from the next song without restart.
- `CheckAsync` also runs `-version`; it feeds the status row. Runs in the background at startup (one log line per program, a console flash when either is missing), on "Check again", after a settings save that moves the folder, and after an install. Announces `FFmpegChanged`.
- **FFMpegCore reads one global folder** (`GlobalFFOptions.BinaryFolder`) for ffprobe; every check points it at the found ffprobe before its first await. The encode does not use it.
- **Installs go into `<AppContext.BaseDirectory>/bin/`** beside `cache/` and `plugins/`: both programs directly there, with `ffmpeg-build.json` naming the build. In a dev run that is a nested `bin/` in the build output — DeepClean keeps any nested `**/bin/**` for that reason. Scratch is `bin/.ffmpeg-install/` (final move stays on one volume).
- **`ffmpeg-builds.json` is the trust root**, embedded in `KHost.Domain` (changes only with a KHost release).
  - One build per `<platform>-<arch>`, exact (no x64 build offered to arm64); each an https URL with a pinned `sha256` and exact `size`.
  - The download stops past the size; the hash is compared **before the archive is opened**; every entry is checked for escapes (`ZipEntryGuard`, shared with the plugin installer); only the two named entries are written, under host-chosen names; each staged program must answer `-version` **before** replacing anything in `bin/`. A failure leaves the old copy.
- **Pinned**: win-x64 (gyan.dev 9.0.2 essentials, via its GitHub release), macos-arm64 (OSXExperts 9.0, code-signed), macos-x64 (evermeet.cx 9.0.2). **Linux: none** — johnvansickle's current URL moves each release and its versioned archive stops at 4.0.3. Never pin a moving "latest".
- **Adding or bumping a build**: download each archive, `shasum -a 256` it, take its byte size, confirm it holds both programs (note entry paths), run both on that platform, cross-check (never copy) the publisher's digest where one exists. Then edit the manifest. `FFmpegBuildManifestTests` checks shape; `FFmpegInstallTests` (integration) installs this machine's build for real.
- **Missing ffmpeg at play → `KH-FFMPEG-MISSING`**, thrown from `HlsMediaStreamService` whether nothing was found or it would not start (otherwise a bare `Win32Exception`). **Missing ffprobe at import** → one flash per run from `MediaImportService`, not a silent row per file.
- macOS: an `HttpClient` download has no quarantine attribute (Gatekeeper skips it), but an unsigned arm64 binary is killed on launch (exit 137, empty stderr); the staged run check catches and reports it.

## Components

- Component logic lives in a code-behind partial (`Foo.razor.cs`, `public partial class Foo`), never an inline `@code` block. `@inject` → `[Inject]` property; `@implements` → interface on the partial. `@page`, `@using`, `@inherits`, `@layout`, `@attribute` stay in the `.razor`.
- Never give the code-behind partial a base class — the generated partial supplies one; a second won't compile.
- `_Imports.razor` does not reach `.razor.cs` — code-behind needs its own `using` directives.
- `Dialog` renders its footer only when one is supplied. A viewer (actions commit on click) supplies none and closes from the header X; no close-only footer button.
- Keyboard shortcuts:
  - A list's arrow keys: Blazor `@onkeydown` on a focusable element *inside* the panel (`tabindex` + `data-kh-keylist`). A handler on the column around the panel never sees it.
  - Global chords: `shortcuts.js`, focusing `[data-kh-shortcut]`, matched in JS so typing never crosses the circuit. None of them act while a dialog is open (`.kh-scrim` in the DOM), and closing the last dialog hands focus back to what held it before (`scroll-utils.js`).
  - Both share `ListKeyboardShortcuts.Resolve`. Every new shortcut must also be added to `KeyboardShortcuts.All` (the menu's dialog is the only place a host discovers one).
- Both queues reorder by dragging the row through `khSortable` (`sortable-interop.js`), keyed per list (one shared instance made two lists tear each other down). It must keep:
  - reverting the DOM to pre-drag order before telling .NET (Blazor diffs against its own tree);
  - filtering the row's `button`s so pressing play/remove is not a drag;
  - `preventOnFilter: false`, or Sortable swallows those buttons' clicks.
  - Row numbers come from a CSS counter, so a reorder renumbers without re-render.
- A boolean is a checkbox: `<input type="checkbox" class="kh-form-check-input">` inside a `label.kh-form-check`, wording in `span.kh-form-check-label`. No slider, no `.kh-checkbox`.
- `ComboBox<TItem>` replaces a native select with type-to-search. Binds the chosen item (not a key), takes rows from a `Search` delegate, labels runs via `GroupName` without reordering (caller groups by sorting). Bind `Text` when the field must accept a value not in the list.
- The console says **song**; the media manager and importer say **media**. `Media` stays the row's name in code.
- Sign-in is off by default, switched on only by `Auth:RequireLogin` in `appsettings.json` (or an env/overlay override). No wizard step or App Settings checkbox.

## CSS/SCSS

- No inline styles or `<style>` elements. BEM with `kh-` prefix (`kh-button--danger`). SCSS nesting. Bootstrap Icons only — no Bootstrap CSS/JS; its utility classes (`d-flex`, `mb-3`, …) resolve to nothing.
- Component styles beside the component (`Foo.razor.scss` → scoped `Foo.razor.css`; gitignored output — never edit or commit it). Shared blocks under `wwwroot/scss` via `app.scss`; a partial co-locates only once exactly one component uses its block.
- Only `app.scss` and `themes/*` may lack a `_` prefix — any other `wwwroot/scss` file without one compiles to its own stylesheet.
- Scoped CSS reaches only elements the component itself renders. A class handed to another component (`<Icon Class="..." />`, `<InputNumber class="..." />`, RenderFragment content) silently matches nothing. Use `::deep` under an ancestor this component renders, naming the child class in full: `.kh-foo__row { ::deep .kh-foo__field { ... } }`. Never `::deep &__x` (`&` expands to the parent and swallows it).
- Panels answer to `@container` (`kh-queue`, `kh-singer-info`, `kh-media-search`) because `panel-resize.js` writes a pixel width (a panel can be 180px at 1440). The header answers to `@media`. Set thresholds against a panel's measured width at 1440, not a round number.
- The console owns the viewport and never scrolls; every other route scrolls as a document. `MainLayout` puts `--scroll` on `.kh-shell` off `/`, releasing the height caps inside it.
  - Release heights only: `.kh-settings-page`'s `flex` is horizontal (it sits in the row `.kh-app__body` lays out), so zeroing it collapses every settings card to content width.
  - A settings page that skips the `kh-app__body` > `kh-settings-page` wrapper grows over the footer.
- `max-width` + `margin-inline: auto` on a flex item kills its stretch; add `width: 100%`.
- A flex item needs `min-width: 0` as well as `white-space: nowrap` to truncate.
- An outline modifier on a filled control must clear the fill too: `.kh-button` sets a `--kh-primary` gradient (overriding only border and text left `--outline-danger` with a primary background under red text).
- `--kh-primary` is not a safe "active" signal — a theme may make it neutral (famicom's is grey). Hence every toggle is a checkbox (`.kh-form-check-input` shows "on" with a check glyph).
- Danger text: `--kh-danger-bright`.
- A `.kh-note` explains **one control and sits directly under it** — never after a run of rows.
  - In a label-beside-control row it goes **inside the label's column** (`<span class="kh-venue-settings__labelled">`, or inside the `.kh-form-check-label` for a checkbox), not as the row's next sibling.
  - With no label column (under a stacked `&__field`, or about a whole section) it is a sibling `<p>`, and the row above drops its bottom margin (`&__row:has(+ .kh-note)`).
  - `.kh-note` and `.kh-app-settings__apply` share type: `display: block`, `0.75rem` at `1.5`, `--kh-text-muted`, space below in `rem` not `em`.
  - No quieter variant: `--kh-text-muted` is the bottom rung.
- `.kh-card__body` pads only a direct `<form>` child; a card body without a form needs its own padding.
- A `<select>` needs `kh-form-select`, not `kh-form-control`, or WebKit draws the native macOS pop-up in the Photino window (a browser looks fine).

## Importing media

- The scanner takes everything the host can play, not only karaoke: break music, ad clips and the between-singers card are ordinary library rows.
- **A `.cdg` with no audio beside it is invalid**, refused twice: the importer skips it and counts it failed; `CompactDiscPlusGraphicsRenderer` fails the play with `KH-CDG-NO-AUDIO` (catches rows already in a library).
  - **`MediaFormats.FindKaraokeAudio` is the one pairing rule**; every asker goes through it. It matches any audio extension, **case-insensitively** (`SONG.CDG` + `song.mp3` is a pair on a case-sensitive filesystem). `IsKaraokeTrack` must agree with it.
  - **`CdgMediaProbe` describes the pair**, reading the audio half for duration and tags. Keep it out of `MediaFileParsingService`. It answers **empty, not null** when the audio is missing.
- **A zipped pair is one song; the zip is the row.** A `.zip` holding exactly one `.cdg` and its same-named audio, flat, imports as Karaoke under either video answer, `FilePath` naming the zip (re-import idempotent by path; nothing unpacked into the library).
  - Pairing inside: `MediaFormats.FindKaraokeAudioAmong`, the names-only half of `FindKaraokeAudio`, also used by the browser's pair rows. Several qualifying audio files → the earliest in `AudioExtensions` wins.
  - Only `__MACOSX/` entries and dot-files are ignored. Anything else (a folder, two pairs, one half, an extra file) is not a song: the importer counts it failed with the reason; `CompactDiscPlusGraphicsRenderer` fails the play with `KH-CDG-ZIP-*` (`SHAPE`, `CORRUPT`, `ENCRYPTED`, `TOO-LARGE`).
  - At play, `ZippedKaraokeSource` (a host `IPlayableMediaSource`, registered in `AddDomain` so it precedes any plugin's) writes the pair into the stream session's directory under **fixed** names (`karaoke.cdg` + `karaoke.<audio ext>`) — an entry name is never a path, so `../` cannot steer a write. Expansion is capped and counted as written. `FindKaraokeAudio` then finds the audio by the loose-pair rule.
  - `CdgMediaProbe` claims the zip and probes the inner audio from a temp file (ffprobe on a pipe gives tags but no duration).
  - **Ask `MediaFormats.IsCompactDiscGraphics` of a library row**, never the `.cdg` extension: a zipped row names the zip, and a `.cdg` check silently loses the unsmoothed CD+G scaling. `IsGraphicsOnlyKaraoke` is for what ffmpeg actually opens.
- **`MediaFormats` owns the extension lists.** `TypeForFile(path, videoIsKaraoke)` decides what a file is; the scanner, the row icon and the import all ask it.
- Asked of the **path**, not the extension: an `.mp3` with a `.cdg` beside it is the audio half of a karaoke pair and does not belong in break music.
- **Video vs. karaoke cannot be settled from the file.** The host answers per folder on the Import button, and row by row through the Type column. Those answers go in `IMediaImportService.TypeOverrides`, keyed by path, and beat anything inferred from the name. Switching the batch answer clears them.

## Tests

- xUnit + NSubstitute; bunit for components.
- Anything outside the process (an external binary like ffmpeg, a live network device) → `KHost.IntegrationTests`. `KHost.UnitTests` stays skip-free. In-process I/O (temp files, in-memory SQLite) stays in unit tests.
- Naming `MethodUnderTest_Scenario_ExpectedBehavior`; substitutes in field initializers; mirror the source layout (`Domain/Services/Foo.cs` → `Domain/Services/FooTests.cs`).
- Test an announcement by subscribing a counter to the real broker the service was built with (`using var subscription = _broker.Subscribe<VenuesChanged>(_ => raised++)`), or substitute `IMessageBroker` and assert `Received(1).Announce(...)`.
- A bunit fixture must register a broker (components `[Inject]` one), or every render throws.
- **A service that starts work in its constructor races a test that stubs a substitute after building it**: the constructor can consume the first call and break an exact call count. Arrange everything before construction, or give the fixture a way to settle the constructor's work.
- **Run the suite under load before trusting green.**
- Times are stored UTC. **Arrange test data in UTC** — a test using the code's local clock cancels the offset and passes. Local time belongs only in a picker's own model and in comparing a converted local date against a local today.
- A component test renders the component (`BunitContext`, not the obsolete `TestContext`) and dispatches a real event — calling a handler directly passes even when it is attached to nothing.
- Set `JSInterop.Mode = JSRuntimeMode.Loose` (panels call JS on first render).
- Give every `Task<List<T>>` substitute a return value: NSubstitute returns a completed task wrapping `null`, and the component `.Count()`s it.
