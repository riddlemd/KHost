# AGENTS.md

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Photino screen app. Solution: `KHost.slnx` (no `.sln`).

Projects (`src/`): `Abstractions` (all interfaces + shared models, no project refs) ← `Domain` (services) / `DataAccess` (EF Core 10 + SQLite) ← `UserInterface` (Blazor Server) and `Screen2` (Photino video output), plus `IPC.SignalR` (UI↔Screen), `Cast` (Chromecast), `Plugins.Sdk` (plugin contracts — no project refs), `LrcLib`, `Telemetry`, `ServiceDefaults`/`AppHost` (Aspire), and `tests/` (`KHost.UnitTests` — hermetic, no skips; `KHost.IntegrationTests` — needs ffmpeg/ffprobe, Cast tests skip without the Chromecast emulator on 127.0.0.1:8009).

## Commands

```bash
dotnet run --project src/KHost.UserInterface                # run the app
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"    # build (redirected so VS's bin/ isn't locked)
dotnet test tests/KHost.UnitTests                           # --filter "FullyQualifiedName~Name" to narrow
dotnet test tests/KHost.IntegrationTests                    # drives real ffmpeg; fails without it (KHOST_SKIP_ENVIRONMENT_TESTS=1 to accept)
```

SCSS compiles inside `dotnet build` (AspNetCore.SassCompiler) — no separate sass step. The build needs `node_modules` (`npm install`) for `copy:vendors`.

## Rules

- Interfaces in `src/KHost.Abstractions` (`Services/`, `Repositories/`, `Models/`); implementations in `src/KHost.Domain` or `src/KHost.DataAccess`. Register in the project's `ProjectExtensions` (`AddDomain()` / `AddDataAccess()`); UI-only services in `Program.cs`. All domain services are singletons — guard mutable state with `SemaphoreSlim`.
- No "gate" services: behaviour that guards a call lives on the service that owns the call (enqueue rules go in `PerformanceService.CreateAndEnqueueAsync`, not an `IEnqueueGuard` around it).
- New repositories/services copy the shape of an existing one: repositories extend `BaseRepository<T>` and implement `SortColumns` / `ApplySearchFilters`; services extend `BaseService` (or `BaseRepositoryService<,>` for CRUD).
- In repositories, `using var context = await ContextFactory.CreateDbContextAsync();` per operation — never store a context.
- Stateful services raise `StateChanged` (`EventHandler`/`EventHandler<T>`, never `event Action`); components subscribe in `OnInitialized`, unsubscribe in `Dispose`, call `StateHasChanged`.
- Member order: fields → events → properties → public → protected → private → nested types.
- Every `Task`/`ValueTask`-returning method ends in `Async` — enforced by reflection in `AsyncNamingConventionTests`; a new project must be a `ProjectReference` of `KHost.UnitTests` to be covered.
- Method names that cross a string boundary (`[JSInvokable]` called from JS, SignalR hub methods invoked by name) break silently when renamed: pass the name as `nameof(...)` from C# and take it as a parameter in JS (see `SingerQueuePanel` / `sortable-interop.js`, `ScreenClient` / `ScreenHub`).
- Library/users/groups persist in SQL; queue and venue state in the JSON cache (`ICacheService`, `./cache/`).
- Dialogs go through `IInteractionDispatcher`, which resolves `IInteractionHandler<TReq, TRes>` from DI; handlers bridge dialogs into awaitable calls with `TaskCompletionSource` and are registered in `Program.cs`.
- Do NOT commit unless explicitly asked.

## Components

- Component logic lives in a code-behind partial (`Foo.razor.cs`, `public partial class Foo`), never an inline `@code` block. `@inject` becomes an `[Inject]` property; `@implements` becomes an interface on the partial. `@page`, `@using`, `@inherits`, `@layout`, `@attribute` stay in the `.razor`.
- Never give the code-behind partial a base class — the generated razor partial already supplies one; a second base clause won't compile.
- `_Imports.razor` does not reach `.razor.cs` — code-behind needs its own `using` directives.
- `ComboBox<TItem>` is the type-to-search replacement for a native select. It binds the chosen item (not a key), takes every row from a `Search` delegate, and labels runs via `GroupName` without reordering them — the caller groups by sorting. Bind `Text` when the field must also accept a value the list does not contain.

## CSS/SCSS

- No inline styles or `<style>` elements. BEM with `kh-` prefix (`kh-button--danger`). SCSS nesting. Bootstrap Icons only — no Bootstrap CSS/JS; its utility classes (`d-flex`, `mb-3`, …) resolve to nothing.
- Component styles live beside the component (`Foo.razor.scss` → scoped `Foo.razor.css`; that output is gitignored — never edit or commit it). Shared blocks stay under `wwwroot/scss` via `app.scss`; a partial co-locates only once exactly one component uses its block. Only `app.scss` and `themes/*` may lack a `_` prefix — any other `wwwroot/scss` file without one compiles to its own stylesheet.
- Scoped CSS reaches only elements the component itself renders. A class handed to another component (`<Icon Class="..." />`, `<InputNumber class="..." />`, RenderFragment content) lands on markup carrying a different scope id or none, and the rule silently matches nothing. Reach it with `::deep` under an ancestor this component does render, naming the child class in full: `.kh-foo__row { ::deep .kh-foo__field { ... } }`. Never `::deep &__x` — `&` expands to the parent and swallows it.
- Narrow layouts key off the right width. Panels answer to `@container` (`kh-queue`, `kh-singer-info`, `kh-media-search`) because `panel-resize.js` writes a pixel width — a panel can be 180px wide at 1440. The header answers to `@media`, having no splitter between it and the viewport. Set thresholds against a panel's measured width at 1440, not a round number.
- A flex item needs `min-width: 0` as well as `white-space: nowrap` before it will truncate; without it, it pushes its neighbours off the row instead.
- `.kh-card__body` pads a direct `<form>` child and nothing else — a card body without a form needs its own padding. A `<select>` needs `kh-form-select`, not `kh-form-control`, or WebKit draws the native macOS pop-up and discards the styling (correct in a browser, wrong only in the Photino window).

## Tests

xUnit + NSubstitute. A test that needs anything outside the process — an external binary (ffmpeg), a live service (the Cast emulator) — belongs in `KHost.IntegrationTests`; `KHost.UnitTests` must stay skip-free so green means everything ran. In-process I/O (temp files, in-memory SQLite) stays in unit tests.

`MethodUnderTest_Scenario_ExpectedBehavior`; substitutes in field initializers; mirror the source layout (`Domain/Services/Foo.cs` → `Domain/Services/FooTests.cs`). Test `StateChanged` with a counter lambda.

## Gotchas

- Services dir is `Services/` — don't recreate the old `Servies` typo.
- Runtime DB lives at `src/KHost.UserInterface/bin/Debug/net10.0/cache/` — deleting `bin/` destroys the local library, users and queue.
- `DefaultContext` is `NoTracking` — saves/updates won't touch related models unless explicitly programmed to.
- `BlazorDisableThrowNavigationException` is set; navigation failures won't throw.
- EF join entities: `UsingEntity<T>(l => ..., r => ..., j => ...)` with no string name — a string name makes a shared-type entity and breaks `context.Set<T>()`.
- IPC screen commands are `[JsonPolymorphic]` on `ScreenCommandBase` — a new command needs a `[JsonDerivedType]` attribute on the base.
- Money is whole cents in an `INTEGER` (`Tip.AmountInCents`) — SQLite has no decimal type, and EF stores one as TEXT, which sorts lexicographically and makes `SUM` coerce through a float.
- The appliance lockdown (no devtools, no page context menu) is gated on the build configuration, not the environment: an unpublished run must stay in Development or it serves no static web assets at all. Test it with `dotnet run -c Release`.
- `Venue.Settings` is a JSON column (`OwnsOne(...ToJson())`): adding/removing properties needs no migration at all, but EF reads keys missing from stored rows as `default` (ignoring property initializers) — a new setting that defaults true needs a data-only `json_set` backfill migration.
- Schema changes (any `DbSet<T>` model): add a migration — `dotnet ef migrations add <Name> --project src/KHost.DataAccess`. Additive ones such as an index apply in place, keeping both the runtime DB and the hand-written `AddMediaFts` (see `AddTipVenueIndex`).
- Regenerating the chain instead (delete `src/KHost.DataAccess/Migrations/` and the runtime DB, then `migrations add InitialSchema`) means recreating `AddMediaFts` by hand afterwards — the FTS5 table and its triggers are raw SQL EF won't regenerate, and search throws `no such table: media_fts` without it; copy the `Up`/`Down` SQL from a prior `AddMediaFts.cs`. It also destroys the local library, users and queue, so collapse the chain deliberately, not as a step in adding a column.
