# AGENTS.md

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Photino screen app. Solution: `KHost.slnx` (no `.sln`).

Projects (`src/`): `Abstractions` (every interface, the shared models, and what a plugin is built against — MIT, no project refs, the bottom layer) ← `Common` (helpers over those contracts, MIT) ← `Domain` (services) / `DataAccess` (EF Core 10 + SQLite) ← `UserInterface` (Blazor Server) and `Screen2` (Photino video output), plus `IPC.SignalR` (UI↔Screen), `Cast` (Chromecast), `LrcLib`, `Telemetry`, `ServiceDefaults`/`AppHost` (Aspire), `tools/` (`KHost.CatalogSync`, the CLI that writes `plugin-catalog.json` entries), `build/` (`KHost.Analyzers`, a netstandard2.0 Roslyn analyzer referenced only at build time), and `tests/` (`KHost.UnitTests` — hermetic, no skips; `KHost.IntegrationTests` — needs ffmpeg/ffprobe, Cast tests skip without the Chromecast emulator on 127.0.0.1:8009).

## Commands

```bash
dotnet run --project src/KHost.UserInterface                # run the app (native Photino window)
dotnet run --project src/KHost.UserInterface -- --headless  # no window; console served at http://localhost:5251
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"    # build (redirected so VS's bin/ isn't locked)
dotnet test tests/KHost.UnitTests                           # --filter "FullyQualifiedName~Name" to narrow
dotnet test tests/KHost.IntegrationTests                    # drives real ffmpeg; fails without it (KHOST_SKIP_ENVIRONMENT_TESTS=1 to accept)

dotnet run --project tools/KHost.CatalogSync -- <owner/repo> # add a plugin's GitHub release to plugin-catalog.json
```

**Prefer `--headless` for testing.** The console is then an ordinary page at `http://localhost:5251`, so it drives with browser tooling and reads with the DOM instead of screenshot coordinate math — the Photino window reaches neither, and a Screen2 window launched over it turns every later capture into a black rectangle. Only the window itself needs the windowed run: native chrome, `SetSize`, and the appliance lockdown. Port 5251 is held by an exclusive `.instance.lock`, so stop one before starting the other.

SCSS compiles inside `dotnet build` (AspNetCore.SassCompiler) — no separate sass step. The build needs `node_modules` (`npm install`) for `copy:vendors`.

## Rules

- Interfaces in `src/KHost.Abstractions` (`Services/`, `Repositories/`, `Models/`); implementations in `src/KHost.Domain` or `src/KHost.DataAccess`. Register in the project's `ProjectExtensions` (`AddDomain()` / `AddDataAccess()`); UI-only services in `Program.cs`. All domain services are singletons — guard mutable state with `SemaphoreSlim`.
- A helper both the host and a plugin would want goes in `KHost.Common`, not `Abstractions`: it is MIT on purpose, so a plugin author may use it without taking PolyForm code into what they redistribute. `Common` is for helpers *over* the contracts — string folding aids, formatting, list surgery, the shared drop-position mechanic. A contract, a model or anything `Abstractions` itself needs belongs in `Abstractions`, which references nothing. `Abstractions` declares, it does not compute — see **No static methods in Abstractions** below. Group by area under `Common` (`Media/`, `Plugins/`) rather than dropping types in its root, and mirror that in the tests. Name its methods for what the call site needs to read, not for what the class already says: a plugin author sees `StreamRate.FromTempo(t)` and `AudioLevels.ClampVolume(v)` without this repo's context, so `For` and `Clamp` are too thin — `PluginRid.MatchesThisHost` names what it matches against, and `int.CentsToCurrencyString()` names the unit the receiver is in. Verbosity here is worth more than symmetry with a BCL name; the one exception is a member that exists to fill a BCL gap (`IList<T>.FindIndex`), where the familiar name *is* the point.
- No "gate" services: behaviour that guards a call lives on the service that owns the call (enqueue rules go in `PerformanceService.CreateAndEnqueueAsync`, not an `IEnqueueGuard` around it).
- New repositories/services copy the shape of an existing one: repositories extend `BaseRepository<T>` and implement `SortColumns` / `ApplySearchFilters`; services extend `BaseService` (or `BaseRepositoryService<,>` for CRUD).
- In repositories, `using var context = await ContextFactory.CreateDbContextAsync();` per operation — never store a context.
- Services announce, they do not raise events. There is no `StateChanged` and no `IKHostService`: a service that has something to say takes `IMessageBroker` in its own constructor (never through `BaseService`, which carries only `ILogger`) and calls `Broker.Announce(new ThingChanged())`. Messages are empty records in `KHost.Abstractions.Messaging.Messages`, one per service, named for the fact — see **Messaging** below.
- Member order: fields → events → properties → public → protected → private → nested types.
- Every `Task`/`ValueTask`-returning method ends in `Async` — enforced by reflection in `AsyncNamingConventionTests`; a new project must be a `ProjectReference` of `KHost.UnitTests` to be covered.
- Method names that cross a string boundary (`[JSInvokable]` called from JS, SignalR hub methods invoked by name) break silently when renamed: pass the name as `nameof(...)` from C# and take it as a parameter in JS (see `SingerQueuePanel` / `sortable-interop.js`, `ScreenClient` / `ScreenHub`).
- Library/users/groups persist in SQL; queue and venue state in the JSON cache (`ICacheService`, `./cache/`).
- Dialogs go through `IInteractionDispatcher`, which resolves `IInteractionHandler<TReq, TRes>` from DI; handlers bridge dialogs into awaitable calls with `TaskCompletionSource` and are registered in `Program.cs`.
- `KHost.Abstractions` and `KHost.Common` are MIT; everything else is PolyForm Shield (`LICENSE`, and each MIT project's own `LICENSE`). `LicenceBoundaryTests` enforces it: an MIT project may reference only MIT projects, and must declare `PackageLicenseExpression` and ship a `LICENSE`. Note the compiler catches only the circular case — a reference to a leaf like `KHost.LrcLib` builds fine and breaks the licence silently, which is what that test is for. There is no separate plugin SDK: a plugin references `Abstractions` and `Common` directly, which is why `Abstractions` may reference nothing at all — `Common` sits above it, never the other way round. `KHostException` lives in `Abstractions` with the interfaces it is thrown across — it is the only way a plugin can report a failure the host can act on, so it has to sit where a plugin can reach it.
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
- Where the existing ones went: `MediaFormats` (reads the disk for a `.cdg` sidecar) and
  `AdPlayback.HasOwnAudio` to `Common/Media/`, alongside `AudioLevels.ClampVolume`,
  `AudioTrackRoles.FromTrackName` and `StreamRate.FromTempo`; `PluginRid` and `PluginVersion` to
  `Common/Plugins/`; `AuthResult`'s factories to `Common/Authentication/AuthResults`;
  `RepositoryModel.IsBuiltIn` to `Common/Repositories/RepositoryModels.IsBuiltIn`.
- `PluginCatalog` is the shape of the split: `IPluginCatalogService` returns it, so the data stays
  in `Abstractions` while `LatestCompatibleRelease`, `HasReleaseForThisHost` and
  `HasReleaseForThisPlatform` became extensions in `Common/Plugins/PluginCatalogExtensions` — they
  needed `PluginRid` and `PluginVersion`, which had already moved.

## Messaging

`IMessageBroker` (`KHost.Abstractions/Messaging/`) is how services and components hear about each other. A plugin can subscribe to what the show is doing, `Abstractions` being what it builds against.

- **`Announce(message)`** is fire-and-forget, for "this moved, redraw". **`await PublishAsync(message)`** waits for every handler and is for the case the publisher's next decision depends on: `PlaybackService` awaits the end-of-performance gap so an ad can claim it before break music comes back.
- Handlers run **one at a time, in subscription order** — what one does decides what the next may do. A handler that throws is logged and skipped: a broken subscriber must not stop the queue reaching the next singer.
- Routing is on the message's **runtime type**, and exact — a handler for a base type is not called for a derived one.
- Subscriptions return `IDisposable`. Hold them in a `SubscriptionSet` and dispose it; a missed unsubscribe keeps a Blazor component — and its whole circuit — alive on the broker.
- Never take a lock around a publish. `ScreenConnected` arrives on the SignalR hub thread already holding one, which is why those handlers are `_ = Task.Run(...)`.
- Components `[Inject] IMessageBroker Broker`, subscribe in `OnInitialized`, dispose the set in `Dispose`.

Three things deliberately stay plain C# events, and should stay that way: Screen2's `IMediaPlayer` and `IScreenClient` (a separate process — the broker is in-process and SignalR is the transport), `IDialogService.ShowRequested` (a request with a payload and one legitimate subscriber, not a notification), and `IPlaybackService.PositionChanged` (twice a second for a whole night; it says only that `Position` moved, so take it to redraw a playhead and nothing else).

## What a plugin can reach

A plugin's entry point is constructed with `ActivatorUtilities.CreateInstance` against the host
container, so it takes whatever it needs from `KHost.Abstractions` in its own constructor — the same
service interfaces the host itself uses. There is deliberately no facade: an `IPluginLibrary`
stood between plugins and the services for a while and only obscured which service actually owned
each rule. `IPluginContext` carries the plugin's own manifest and stored settings, and nothing else.

- Downloading media for the queue goes through `IMediaAcquisitionService` — an ordinary service, not
  a plugin keyhole. It owns three rules nothing else may re-implement: an import is idempotent by
  `FilePath`, the media row's status and the `IDownloadsService` entry move together, and
  `DiscardImportAsync` deletes only a row still in `Downloading`. Enqueuing is *not* on it: a caller
  composes `ISingerQueueService.SelectedUserId` with `IPerformanceService.CreateAndEnqueueAsync`,
  because `SingerQueueService` already depends on `IPerformanceService` and folding the pair into
  either one closes a constructor cycle.
- A plugin that needs a value it must not persist — a login it will trade for a session key and
  hold only in memory — injects `IInteractionDispatcher` (same as the host) and sends a
  `TextPromptRequest`. Unlike a plugin setting, nothing in that round trip ever reaches
  `plugins.json`; the dialog is the only place the value exists outside the plugin's own
  variables, for exactly as long as answering the request takes. `KHost.Plugins.KaraFun`'s sign-in
  action is the first user of it.
- A plugin puts **buttons on its Plugins-page row** by declaring them in the manifest
  (`PluginButtonDefinition`, key + label + optional style) and implementing `IPluginButtonHandler`.
  The host runs `InvokeButtonAsync(key)` on click and re-reads `DescribeButton(key)` after, so a
  single button can toggle its own label — KaraFun's reads "Sign in" or "Sign out" from whether a
  session is open. `DescribeButton` also hides or disables a button; its default keeps the manifest
  label. Reached by plugin id through `IPluginButtonService`, populated from the loader's
  `PluginButtonBinding`s — the container does not otherwise record which plugin owns a registration.
- A plugin that renders media it must not let play without a live entitlement implements
  `IMediaPlaybackGate`: it stamps its files with the container tag `IMediaPlaybackGate.MetadataTag`
  (`khost_provider`) set to its own `ProviderId`, and `PlaybackService.LoadAsync` asks the matching gate
  before every load. KaraFun writes `khost_provider=KHost.Plugins.KaraFun` into the file (with ffmpeg's
  `+use_metadata_tags` movflag — a custom mp4 tag is dropped without it, confirmed by round trip)
  and its gate allows play only while signed in. Its output also carries a `.khv` extension rather
  than `.mp4` (the muxer forced with `-f mp4`, since ffmpeg picks the output format from the name):
  obscurity so the licensed content does not open on a double-click, not protection — KHost reads
  media by content, not extension. `IMediaGateService` reads the tag and routes to
  the gate whose `ProviderId` matches; a file with no tag, or one no loaded gate claims, always plays.
  A block refuses the load like a non-Ready row and flashes the gate's reason — nothing on screen
  says why otherwise. The check runs on every load, so a gate stays cheap (the in-memory answer,
  not a round trip) unless the content is worth one.
- **A plugin extension type is one singleton, shared across every extension interface it
  implements.** The loader registers the concrete type once and points each interface at it, so
  KaraFun's `KaraFunMediaProvider` is its `IMediaProvider` search, its `IPluginButtonHandler`
  session button, and its `IMediaPlaybackGate` all at once — one object, one `_sessionKey`, so
  signing in anywhere gates everywhere. Registering per interface instead (the old shape) built the
  type once for each, and signing in on the button would not have signed in the search.

## Plugin catalog and installs

The Available tab on the Plugins page installs from a published `plugin-catalog.json` (this repo's root,
served raw from `master`; `PluginCatalog:Url`). The catalog is the **trust root** — a plugin runs
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
  dependencies. Ship no `.pdb`, and no copy of `KHost.Abstractions.dll` or `KHost.Common.dll`: `PluginLoadContext.Load`
  returns null for anything already in the default context, so a plugin-local copy of either is never loaded.
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

## The screen marquee

The band of text across the screen. `ScreenMarqueeService` composes it and pushes it whole on
every change — there is no patch command, so a screen that reconnects mid-show is correct after
one `SetMarqueeCommand`.

- It is **one line, always**. `white-space: nowrap` is restated on the track and its spans rather
  than left to inherit, and the venue's message is collapsed to a single line host-side, because
  the band is fixed to an edge: a second line grows over the picture instead of pushing the page.
- Entries are built from the venue's `MarqueeEntryFormat` — `{song} - {singer}` when a venue has
  never set one, since a blank format would compose empty lines rather than reading as "unset".
  Tags (`{song}`, `{artist}`, `{singer}`, `{position}`, matched case-insensitively) are replaced
  per singer from the queue order and their first queued performance. A singer with nothing queued
  is named alone rather than run through the format — the host has them on the list, and the band
  must not disagree with the queue on screen.
- Every marquee venue setting reads as "off" when its key is missing (`false`, `0`, `null`), so
  the feature needed **no backfill migration** — see the `Venue.Settings` note above. Zero means
  "the screen decides" for size and speed; the dialog offers the screen's own values instead,
  which a number input can show and zero cannot.
- The screen holds no library and no queue, so the command carries finished strings and CSS
  colours, never ids — the same reason `ShowImageCommand` carries a URL.
- Colours come from the venue as `--marquee-bg` / `--marquee-fg`, and the band's own colour sits
  on a `::before` layer rather than its background: the layer can then be held under full opacity
  without taking the text down with it.
- Speed is pixels a second, not a lap time. A duration would make a long line race to keep a
  short one's pace.
- `MarqueePinLabel` holds "Up next" at the leading edge instead of scrolling it past, and the
  renderer then leaves it out of the track — pinned and scrolling are the same label, so drawing
  both puts it on screen twice. It is a **modifier**, not a style: it changes where the label sits,
  not how the band is painted, so it composes with whatever else the venue chose.

## Screens on startup, and where their windows sit

- `LocalScreen:LaunchOnStartup` (the App Settings page's "Open a screen when KHost starts") opens
  one screen named `AppSettings.StartupScreenName`. Read once on the way up, so changing it flips
  `RestartRequired` rather than pretending to take effect.
- **Resolving the listening address and launching that screen are one `ApplicationStarted`
  callback, in that order, and must stay one.** `ApplicationStarted` is a `CancellationToken`, and
  its callbacks run in **reverse registration order** — registering the launch "after" the
  resolution ran it *first*, so the screen carried `LocalScreenProvider.ServiceOptions`' default
  `http://localhost:5000/ipc/screen` while the host was on another port. That is not a slow start
  a screen recovers from: `ScreenClient` tries its first connection once, and
  `WithAutomaticReconnect` covers a connection that was established, not one that never was — so a
  wrong address at launch is a screen that sits there forever saying it lost the host.
- A screen remembers its own window in `cache/screens/<screen id>.window.json`, **on the machine
  the window is on**. Not host-side: a screen on another machine keeps its own place, and one
  started by hand remembers as much as one the host launched.
- Written as the window moves, never on the way out. `LocalScreenProvider.CloseSpawnedScreens`
  kills the process, so an exit handler would never run on the ordinary path — Photino's
  location/size/maximized/restored handlers schedule a debounced write instead.
- Full screen is stored as a flag, not the monitor's bounds: the screen may come back on a
  different monitor, where yesterday's pixels would leave it part-way off the picture. It is
  applied when the page reports ready, since resizing needs a window that exists.
- A stored window with no width or height is treated as nothing stored — it would open invisible
  and could not be dragged back.

## Components

- Component logic lives in a code-behind partial (`Foo.razor.cs`, `public partial class Foo`), never an inline `@code` block. `@inject` becomes an `[Inject]` property; `@implements` becomes an interface on the partial. `@page`, `@using`, `@inherits`, `@layout`, `@attribute` stay in the `.razor`.
- Never give the code-behind partial a base class — the generated razor partial already supplies one; a second base clause won't compile.
- `_Imports.razor` does not reach `.razor.cs` — code-behind needs its own `using` directives.
- `Dialog` renders its footer only when one is supplied. A viewer — one whose actions commit as they are clicked — supplies none and closes from the header X; a footer button that only closes is furniture.
- Keyboard shortcuts split two ways. A list's arrow keys are a Blazor `@onkeydown` on a focusable element *inside* the panel (`tabindex` + `data-kh-keylist`): keydown fires on the focused element and bubbles up, so a handler on the column around the panel never sees it. Global chords live in `shortcuts.js` and focus `[data-kh-shortcut]`, matched in JS so ordinary typing never crosses the circuit. Both lists share `ListKeyboardShortcuts.Resolve`. A new shortcut has to reach `KeyboardShortcuts.All` as well — the dialog off the menu is the only place a host can discover one.
- Both queues reorder by dragging the row itself through `khSortable` (`sortable-interop.js`), keyed per list — it held one instance, so two sortable lists on screen had each init tear the other down. Three things it has to keep doing: revert the DOM to its pre-drag order before telling .NET (Blazor diffs against its own tree and SortableJS moved nodes behind it), filter the row's `button`s so a press on play or remove is not a drag, and keep `preventOnFilter: false` or Sortable swallows those buttons' clicks along with the drag. Row numbers come from a CSS counter, so a reorder renumbers without a re-render.
- A boolean is a checkbox — `<input type="checkbox" class="kh-form-check-input">` inside a `label.kh-form-check`, with a `span.kh-form-check-label` for its wording. There is no slider: one lived on the Plugins page, a second was hoisted to a shared partial for the Themes page, and alongside them sat an unstyled `.kh-checkbox` and four bare inputs that rendered the browser's own blue box. One control, one class.
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
- `.kh-card__body` pads a direct `<form>` child and nothing else — a card body without a form needs its own padding. A `<select>` needs `kh-form-select`, not `kh-form-control`, or WebKit draws the native macOS pop-up and discards the styling (correct in a browser, wrong only in the Photino window).

## Tests

xUnit + NSubstitute, and bunit for components. A test that needs anything outside the process — an external binary (ffmpeg), a live service (the Cast emulator) — belongs in `KHost.IntegrationTests`; `KHost.UnitTests` must stay skip-free so green means everything ran. In-process I/O (temp files, in-memory SQLite) stays in unit tests.

`MethodUnderTest_Scenario_ExpectedBehavior`; substitutes in field initializers; mirror the source layout (`Domain/Services/Foo.cs` → `Domain/Services/FooTests.cs`). Test an announcement by subscribing a counter to the real broker the service was built with (`using var subscription = _broker.Subscribe<VenuesChanged>(_ => raised++)`), or substitute `IMessageBroker` and assert `Received(1).Announce(...)`. A bunit fixture must register a broker — components `[Inject]` one — or every render throws on the missing service.

A component test renders the component (`BunitContext`, not the obsolete `TestContext`) and dispatches a real event — a handler that exists but is attached to nothing passes every test that calls it directly, which is how the queue's arrow keys sat dead behind tooltips advertising them. Set `JSInterop.Mode = JSRuntimeMode.Loose` (panels call into JS on first render) and give every `Task<List<T>>` substitute a return value: NSubstitute hands back a completed task wrapping `null`, and the component `.Count()`s it.

## Gotchas

- Services dir is `Services/` — don't recreate the old `Servies` typo.
- Runtime DB lives at `src/KHost.UserInterface/bin/Debug/net10.0/cache/` — deleting `bin/` destroys the local library, users and queue.
- Machine settings are an overlay at `cache/settings.json`, edited by the App Settings page through `IAppSettingsService` and reloaded on change; deployment defaults stay in `appsettings.json`. Per-list page sizes live there under `Pagination:` and are clamped on read as well as on save, because a hand-edited `0` reaches `PaginatedResult` as a page that holds no rows and reports no pages.
- Seed test data through the repositories, not the `sqlite3` CLI: the system binary has no fts5, so any Media insert dies on the `media_fts` triggers, and the folded columns are written by `EntityFolding` on save rather than by hand. A throwaway console app referencing `KHost.DataAccess` with `AddDataAccess()` gets both — point it at the runtime DB by symlinking `cache` into its output directory, since `DatabaseLocation` reads `AppContext.BaseDirectory`.
- `DefaultContext` is `NoTracking` — saves/updates won't touch related models unless explicitly programmed to.
- `BlazorDisableThrowNavigationException` is set; navigation failures won't throw.
- EF join entities: `UsingEntity<T>(l => ..., r => ..., j => ...)` with no string name — a string name makes a shared-type entity and breaks `context.Set<T>()`.
- IPC screen commands are `[JsonPolymorphic]` on `ScreenCommandBase` — a new command needs a `[JsonDerivedType]` attribute on the base.
- Times are stored UTC and converted where they are shown. `DateTime.Now` against a stored timestamp shifts by the host's offset — it moved the duplicate-song window by five hours here — and a test that arranges its data with the same local clock cancels the error and passes. Arrange in UTC, and remember such a test can only fail on a machine that is not already at UTC. Local time is right in exactly two places: a picker's own model (converted on save and load) and comparing a converted local date against a local today.
- `MediaSearchEntity` lives in `KHost.Abstractions`, so it is a contract with providers outside this repo: changing it breaks their build. It carries `Title` and `Artist` separately — the library stores them apart, and rejoining them means the console has to re-parse a string it built. `ForeignKey` is the provider's own key, and only a local result's is already a library id; `Performance.MediaId` is a Guid into the library, so a remote result has to be imported before it can be enqueued.
- Only `Ready` and `Broken` are a host's to set (`MediaStatusDisplay.IsUserSettable`). Nothing writes `Downloading` or `Processing` yet, so a status control that lists them describes a pipeline that does not exist — leaving those states belongs to whatever provider eventually sets them.
- Money is whole cents in an `INTEGER` (`Tip.AmountInCents`) — SQLite has no decimal type, and EF stores one as TEXT, which sorts lexicographically and makes `SUM` coerce through a float.
- The appliance lockdown (no devtools, no page context menu) is gated on the build configuration, not the environment: an unpublished run must stay in Development or it serves no static web assets at all. Test it with `dotnet run -c Release`.
- Two venue messages, and picking the wrong one is a bug you will not see in a test that only checks the happy path. `VenuesChanged` says the list moved (add/edit/delete) and is for the UI. `SelectedVenueChanged` says the console is now running a different venue, or the one it is running was edited — that is the one `ScreenCoordinationService` and `BreakMusicService` take, because the venue carries the room's audio baseline. Subscribing them to `VenuesChanged` means editing an unrelated venue's phone number re-pushes volume to every screen mid-song.
- - `Venue.Settings` is a JSON column (`OwnsOne(...ToJson())`): adding/removing properties needs no migration at all, but EF reads keys missing from stored rows as `default` (ignoring property initializers) — a new setting that defaults true needs a data-only `json_set` backfill migration.
- Schema changes (any `DbSet<T>` model): add a migration — `dotnet ef migrations add <Name> --project src/KHost.DataAccess`. Additive ones such as an index apply in place, keeping both the runtime DB and the hand-written `AddMediaFts`.
- Regenerating the chain instead (delete `src/KHost.DataAccess/Migrations/` and the runtime DB, then `migrations add InitialSchema`) means recreating `AddMediaFts` by hand afterwards — the FTS5 table and its triggers are raw SQL EF won't regenerate, and search throws `no such table: media_fts` without it; copy the `Up`/`Down` SQL from a prior `AddMediaFts.cs`. It also destroys the local library, users and queue, so collapse the chain deliberately, not as a step in adding a column.
