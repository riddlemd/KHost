# AGENTS.md

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Photino screen app. Solution: `KHost.slnx` (no `.sln`).

Projects (`src/`): `Abstractions` (all interfaces + shared models, no project refs) ← `Domain` (services) / `DataAccess` (EF Core 10 + SQLite) ← `UserInterface` (Blazor Server) and `Screen2` (Photino video output), plus `IPC.SignalR` (UI↔Screen), `Cast` (Chromecast), `Plugins.Sdk` (plugin contracts — no project refs), `LrcLib`, `Telemetry`, `ServiceDefaults`/`AppHost` (Aspire), and `tests/KHost.UnitTests`.

## Commands

```bash
dotnet run --project src/KHost.UserInterface                # run the app
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"    # build (redirected so VS's bin/ isn't locked)
dotnet test tests/KHost.UnitTests                           # --filter "FullyQualifiedName~Name" to narrow
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

## CSS/SCSS

- No inline styles or `<style>` elements. BEM with `kh-` prefix (`kh-button--danger`). SCSS nesting. Bootstrap Icons only — no Bootstrap CSS/JS; its utility classes (`d-flex`, `mb-3`, …) resolve to nothing.
- Component styles live beside the component (`Foo.razor.scss` → scoped `Foo.razor.css`; that output is gitignored — never edit or commit it). Shared blocks stay under `wwwroot/scss` via `app.scss`; a partial co-locates only once exactly one component uses its block. Only `app.scss` and `themes/*` may lack a `_` prefix — any other `wwwroot/scss` file without one compiles to its own stylesheet.
- Scoped CSS reaches only elements the component itself renders. Markup handed to a child (`<Icon Class="..." />`, RenderFragment content) carries the CHILD's scope id — style it via `::deep` with a real descendant: `.kh-foo { ::deep &__icon { ... } }`. Never lead with `::deep &__x` — the `&` swallows the parent and the browser drops the rule.

## Tests

xUnit + NSubstitute. `MethodUnderTest_Scenario_ExpectedBehavior`; substitutes in field initializers; mirror the source layout (`Domain/Services/Foo.cs` → `Domain/Services/FooTests.cs`). Test `StateChanged` with a counter lambda.

## Gotchas

- Services dir is `Services/` — don't recreate the old `Servies` typo.
- Runtime DB lives at `src/KHost.UserInterface/bin/Debug/net10.0/cache/` — deleting `bin/` destroys the local library, users and queue.
- `DefaultContext` is `NoTracking` — saves/updates won't touch related models unless explicitly programmed to.
- `BlazorDisableThrowNavigationException` is set; navigation failures won't throw.
- EF join entities: `UsingEntity<T>(l => ..., r => ..., j => ...)` with no string name — a string name makes a shared-type entity and breaks `context.Set<T>()`.
- IPC screen commands are `[JsonPolymorphic]` on `ScreenCommandBase` — a new command needs a `[JsonDerivedType]` attribute on the base.
- `Venue.Settings` is a JSON column (`OwnsOne(...ToJson())`): adding/removing properties needs no migration reset, but EF reads keys missing from stored rows as `default` (ignoring property initializers) — a new setting that defaults true needs a data-only `json_set` backfill migration.
- Real schema changes (any `DbSet<T>` model): delete `src/KHost.DataAccess/Migrations/` and the runtime DB, run `dotnet ef migrations add InitialSchema --project src/KHost.DataAccess`, then recreate `AddMediaFts` by hand — the FTS5 table and its triggers are raw SQL EF won't regenerate (search throws `no such table: media_fts` without it); copy the `Up`/`Down` SQL from a prior `AddMediaFts.cs`.
