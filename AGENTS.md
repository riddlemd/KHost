# AGENTS.md

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Photino screen app. Solution: `KHost.slnx` (no `.sln`).

Projects (`src/`): `Plugins.Sdk` (plugin contracts + the message broker — no project refs, so it is the bottom layer everything may reference) ← `Abstractions` (all interfaces + shared models) ← `Domain` (services) / `DataAccess` (EF Core 10 + SQLite) ← `UserInterface` (Blazor Server) and `Screen2` (Photino video output), plus `IPC.SignalR` (UI↔Screen), `Cast` (Chromecast), `LrcLib`, `Telemetry`, `ServiceDefaults`/`AppHost` (Aspire), and `tests/` (`KHost.UnitTests` — hermetic, no skips; `KHost.IntegrationTests` — needs ffmpeg/ffprobe, Cast tests skip without the Chromecast emulator on 127.0.0.1:8009).

## Commands

```bash
dotnet run --project src/KHost.UserInterface                # run the app (native Photino window)
dotnet run --project src/KHost.UserInterface -- --headless  # no window; console served at http://localhost:5251
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"    # build (redirected so VS's bin/ isn't locked)
dotnet test tests/KHost.UnitTests                           # --filter "FullyQualifiedName~Name" to narrow
dotnet test tests/KHost.IntegrationTests                    # drives real ffmpeg; fails without it (KHOST_SKIP_ENVIRONMENT_TESTS=1 to accept)
```

**Prefer `--headless` for testing.** The console is then an ordinary page at `http://localhost:5251`, so it drives with browser tooling and reads with the DOM instead of screenshot coordinate math — the Photino window reaches neither, and a Screen2 window launched over it turns every later capture into a black rectangle. Only the window itself needs the windowed run: native chrome, `SetSize`, and the appliance lockdown. Port 5251 is held by an exclusive `.instance.lock`, so stop one before starting the other.

SCSS compiles inside `dotnet build` (AspNetCore.SassCompiler) — no separate sass step. The build needs `node_modules` (`npm install`) for `copy:vendors`.

## Rules

- Interfaces in `src/KHost.Abstractions` (`Services/`, `Repositories/`, `Models/`); implementations in `src/KHost.Domain` or `src/KHost.DataAccess`. Register in the project's `ProjectExtensions` (`AddDomain()` / `AddDataAccess()`); UI-only services in `Program.cs`. All domain services are singletons — guard mutable state with `SemaphoreSlim`.
- No "gate" services: behaviour that guards a call lives on the service that owns the call (enqueue rules go in `PerformanceService.CreateAndEnqueueAsync`, not an `IEnqueueGuard` around it).
- New repositories/services copy the shape of an existing one: repositories extend `BaseRepository<T>` and implement `SortColumns` / `ApplySearchFilters`; services extend `BaseService` (or `BaseRepositoryService<,>` for CRUD).
- In repositories, `using var context = await ContextFactory.CreateDbContextAsync();` per operation — never store a context.
- Services announce, they do not raise events. There is no `StateChanged` and no `IKHostService`: a service that has something to say takes `IMessageBroker` in its own constructor (never through `BaseService`, which carries only `ILogger`) and calls `Broker.Announce(new ThingChanged())`. Messages are empty records in `KHost.Plugins.Sdk.Messaging.Messages`, one per service, named for the fact — see **Messaging** below.
- Member order: fields → events → properties → public → protected → private → nested types.
- Every `Task`/`ValueTask`-returning method ends in `Async` — enforced by reflection in `AsyncNamingConventionTests`; a new project must be a `ProjectReference` of `KHost.UnitTests` to be covered.
- Method names that cross a string boundary (`[JSInvokable]` called from JS, SignalR hub methods invoked by name) break silently when renamed: pass the name as `nameof(...)` from C# and take it as a parameter in JS (see `SingerQueuePanel` / `sortable-interop.js`, `ScreenClient` / `ScreenHub`).
- Library/users/groups persist in SQL; queue and venue state in the JSON cache (`ICacheService`, `./cache/`).
- Dialogs go through `IInteractionDispatcher`, which resolves `IInteractionHandler<TReq, TRes>` from DI; handlers bridge dialogs into awaitable calls with `TaskCompletionSource` and are registered in `Program.cs`.
- Do NOT commit unless explicitly asked.

## Messaging

`IMessageBroker` (`KHost.Plugins.Sdk/Messaging/`) is how services and components hear about each other. It lives in the SDK, not `Abstractions`, so a plugin can subscribe to what the show is doing.

- **`Announce(message)`** is fire-and-forget, for "this moved, redraw". **`await PublishAsync(message)`** waits for every handler and is for the case the publisher's next decision depends on: `PlaybackService` awaits the end-of-performance gap so an ad can claim it before break music comes back.
- Handlers run **one at a time, in subscription order** — what one does decides what the next may do. A handler that throws is logged and skipped: a broken subscriber must not stop the queue reaching the next singer.
- Routing is on the message's **runtime type**, and exact — a handler for a base type is not called for a derived one.
- Subscriptions return `IDisposable`. Hold them in a `SubscriptionSet` and dispose it; a missed unsubscribe keeps a Blazor component — and its whole circuit — alive on the broker.
- Never take a lock around a publish. `ScreenConnected` arrives on the SignalR hub thread already holding one, which is why those handlers are `_ = Task.Run(...)`.
- Components `[Inject] IMessageBroker Broker`, subscribe in `OnInitialized`, dispose the set in `Dispose`.

Three things deliberately stay plain C# events, and should stay that way: Screen2's `IMediaPlayer` and `IScreenClient` (a separate process — the broker is in-process and SignalR is the transport), `IDialogService.ShowRequested` (a request with a payload and one legitimate subscriber, not a notification), and `IPlaybackService.PositionChanged` (twice a second for a whole night; it says only that `Position` moved, so take it to redraw a playhead and nothing else).

## Components

- Component logic lives in a code-behind partial (`Foo.razor.cs`, `public partial class Foo`), never an inline `@code` block. `@inject` becomes an `[Inject]` property; `@implements` becomes an interface on the partial. `@page`, `@using`, `@inherits`, `@layout`, `@attribute` stay in the `.razor`.
- Never give the code-behind partial a base class — the generated razor partial already supplies one; a second base clause won't compile.
- `_Imports.razor` does not reach `.razor.cs` — code-behind needs its own `using` directives.
- `Dialog` renders its footer only when one is supplied. A viewer — one whose actions commit as they are clicked — supplies none and closes from the header X; a footer button that only closes is furniture.
- Keyboard shortcuts split two ways. A list's arrow keys are a Blazor `@onkeydown` on a focusable element *inside* the panel (`tabindex` + `data-kh-keylist`): keydown fires on the focused element and bubbles up, so a handler on the column around the panel never sees it. Global chords live in `shortcuts.js` and focus `[data-kh-shortcut]`, matched in JS so ordinary typing never crosses the circuit. Both lists share `ListKeyboardShortcuts.Resolve`. A new shortcut has to reach `KeyboardShortcuts.All` as well — the dialog off the menu is the only place a host can discover one.
- Both queues reorder by dragging the row itself through `khSortable` (`sortable-interop.js`), keyed per list — it held one instance, so two sortable lists on screen had each init tear the other down. Three things it has to keep doing: revert the DOM to its pre-drag order before telling .NET (Blazor diffs against its own tree and SortableJS moved nodes behind it), filter the row's `button`s so a press on play or remove is not a drag, and keep `preventOnFilter: false` or Sortable swallows those buttons' clicks along with the drag. Row numbers come from a CSS counter, so a reorder renumbers without a re-render.
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
- `MediaSearchEntity` lives in `KHost.Plugins.Sdk`, so it is a contract with providers outside this repo: changing it breaks their build. It carries `Title` and `Artist` separately — the library stores them apart, and rejoining them means the console has to re-parse a string it built. `ForeignKey` is the provider's own key, and only a local result's is already a library id; `Performance.MediaId` is a Guid into the library, so a remote result has to be imported before it can be enqueued.
- Only `Ready` and `Broken` are a host's to set (`MediaStatusDisplay.IsUserSettable`). Nothing writes `Downloading` or `Processing` yet, so a status control that lists them describes a pipeline that does not exist — leaving those states belongs to whatever provider eventually sets them.
- Money is whole cents in an `INTEGER` (`Tip.AmountInCents`) — SQLite has no decimal type, and EF stores one as TEXT, which sorts lexicographically and makes `SUM` coerce through a float.
- The appliance lockdown (no devtools, no page context menu) is gated on the build configuration, not the environment: an unpublished run must stay in Development or it serves no static web assets at all. Test it with `dotnet run -c Release`.
- Two venue messages, and picking the wrong one is a bug you will not see in a test that only checks the happy path. `VenuesChanged` says the list moved (add/edit/delete) and is for the UI. `SelectedVenueChanged` says the console is now running a different venue, or the one it is running was edited — that is the one `ScreenCoordinationService` and `BreakMusicService` take, because the venue carries the room's audio baseline. Subscribing them to `VenuesChanged` means editing an unrelated venue's phone number re-pushes volume to every screen mid-song.
- - `Venue.Settings` is a JSON column (`OwnsOne(...ToJson())`): adding/removing properties needs no migration at all, but EF reads keys missing from stored rows as `default` (ignoring property initializers) — a new setting that defaults true needs a data-only `json_set` backfill migration.
- Schema changes (any `DbSet<T>` model): add a migration — `dotnet ef migrations add <Name> --project src/KHost.DataAccess`. Additive ones such as an index apply in place, keeping both the runtime DB and the hand-written `AddMediaFts`.
- Regenerating the chain instead (delete `src/KHost.DataAccess/Migrations/` and the runtime DB, then `migrations add InitialSchema`) means recreating `AddMediaFts` by hand afterwards — the FTS5 table and its triggers are raw SQL EF won't regenerate, and search throws `no such table: media_fts` without it; copy the `Up`/`Down` SQL from a prior `AddMediaFts.cs`. It also destroys the local library, users and queue, so collapse the chain deliberately, not as a step in adding a column.
