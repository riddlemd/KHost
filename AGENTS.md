# AGENTS.md

This file provides guidance to coding agents working in this repository.

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Avalonia desktop screen app. Solution file: `KHost.slnx` (no `.sln`).

## Commands

```bash
dotnet run --project src/KHost.UserInterface   # run UI directly
dotnet run --project src/KHost.AppHost         # run via Aspire
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"  # build whole solution (redirects output to avoid locking VS's bin/ folder)
dotnet test tests/KHost.UnitTests                # run all unit tests
dotnet test tests/KHost.UnitTests --filter "FullyQualifiedName~ServiceName"  # run tests for one class
dotnet test tests/KHost.UnitTests --filter "DisplayName~MethodName"          # run a single test by method
```

SCSS only — no full rebuild needed:
```bash
cd src/KHost.UserInterface && npm run sass     # one-shot compile
```

## Architecture

Dependency direction: **UI → Domain / DataAccess → Abstractions**. `KHost.Abstractions` has no project references.

Source projects live under `src/`; test projects live under `tests/`.

| Project | Role |
|---|---|
| `KHost.Abstractions` | All interfaces and shared models. File-scoped namespaces. |
| `KHost.Domain` | Business logic, services, domain models. Registered via `AddDomain()`. |
| `KHost.DataAccess` | EF Core 10 + SQLite. `DefaultContext`, `BaseRepository<T>`. Registered via `AddDataAccess()`. |
| `KHost.UserInterface` | Blazor Server app. Interactive Server render mode. |
| `KHost.Screen` | Avalonia desktop app for video output. Launched with `--server-uri` and `--screen-id` args. |
| `KHost.IPC.SignalR` | SignalR hub + client for UI↔Screen command/state exchange. |
| `KHost.LrcLib` | HTTP client for lrclib.net lyrics lookup. |
| `KHost.Telemetry` | OpenTelemetry metrics/activities via `KHostMetrics` and `KHostActivitySource`. |
| `KHost.ServiceDefaults` | Aspire service defaults (logging, health checks). |
| `KHost.AppHost` | Aspire orchestration host. |
| `KHost.UnitTests` | xUnit + NSubstitute. |

## Conventions

**Interfaces → `src/KHost.Abstractions/Services/` or `src/KHost.Abstractions/Models/`.** Concrete implementations go in `src/KHost.Domain/` or `src/KHost.DataAccess/`.

**DI registration** — each project exposes a `ProjectExtensions` method (`AddDomain()`, `AddDataAccess()`). All domain services are singletons.

**Repositories** — `BaseRepository<T>` takes `IDbContextFactory<DefaultContext>`. Always `using var context = await ContextFactory.CreateDbContextAsync();` per operation — never store a context.

**State persistence** — SQL (`DefaultContext`) for library/users/groups. JSON cache under `./cache/` (`ICacheService`) for queue and venue state.

**Events** — stateful services expose `StateChanged`; Blazor components subscribe in `OnInitialized` and call `StateHasChanged`. Services inherit `BaseService` (provides `ILogger Logger` and `protected void InvokeStateChanged()`). CRUD services inherit `BaseRepositoryService<TService, TRepository>` which wraps the repository and calls `InvokeStateChanged()` after mutations.

**EF Core join entities** — use `UsingEntity<T>(l => ..., r => ..., j => { ... })` (no string name argument). Add `j.ToTable(...)` explicitly if the table name needs to differ from the CLR type name. Using a string name makes it a shared-type entity and breaks `context.Set<T>()`.

## C# Member Order

Fields → Events → Properties (auto-props first) → Public methods → Internal/Protected methods → Private methods → Nested types.

`IDisposable`: place `Dispose()` and `protected virtual Dispose(bool)` at the end of public methods.

Explicit interface implementations: place immediately after the public overload of the same method.

**Events:** use `EventHandler` / `EventHandler<T>`, never `event Action` or `event Func`.

## CSS/SCSS

- No inline styles. No `<style>` elements. All styles go in `.scss` files.
- Bootstrap Icons only — no Bootstrap CSS or JS.
- BEM naming. Top-level classes prefix: `kh-`. Button classes prefix: `btn-`.
- Use SCSS nesting, not flat CSS.

## Git

Do NOT commit unless explicitly asked.

## Adding a new repository

1. Create interface in `src/KHost.Abstractions/Repositories/`.
2. Create concrete class in `src/KHost.DataAccess/Repositories/` extending `BaseRepository<T>`.
3. Implement `SortColumns` (maps string keys to `Expression<Func<T, object>>` for sort), `DefaultSortExpression`, `DefaultSortDescending`, and `ApplySearchFilters<TOptions>` (add WHERE clauses before search executes).
4. Register in `src/KHost.DataAccess/ProjectExtensions.cs`.

## Adding a new domain service

1. Create interface in `src/KHost.Abstractions/Services/`.
2. Create class in `src/KHost.Domain/Services/` extending `BaseService` (or `BaseRepositoryService<,>` for CRUD wrappers).
3. Register as singleton in `src/KHost.Domain/ProjectExtensions.cs`.

## IPC (UI ↔ Screen)

Commands flow **UI → Screen**; state flows **Screen → UI**.

- `KHost.IPC.SignalR` provides `ScreenHub` (ASP.NET Core SignalR hub) and `ScreenClient` (Avalonia-side client).
- Commands (`LoadMediaCommand`, `PlayCommand`, `PauseCommand`, `StopCommand`, `SeekCommand`, `SetVolumeCommand`, `SetPitchCommand`) inherit `ScreenCommandBase` and are serialized as JSON strings with a `$type` discriminator via `[JsonPolymorphic]` / `[JsonDerivedType]` attributes on the base — **adding a new command requires adding a `[JsonDerivedType]` attribute**.
- `IScreenServer.BroadcastCommandAsync(IScreenCommand)` sends to all connected screens.
- `ScreenIpcController` in `KHost.Screen` dispatches received commands to `IMediaPlayer` via a `switch` on the concrete command type, then sends updated `ScreenPlaybackState` back.
- Server registered via `AddSignalRIPCServer()` + `MapIPCServer("/ipc/screen")`.

## Dialog/Interaction system

`IInteractionDispatcher.HandleAsync<TRequest, TResponse>()` resolves the matching `IInteractionHandler<TRequest, TResponse>` from DI and invokes it. Handlers use `TaskCompletionSource` to bridge event-driven UI dialogs into awaitable calls. Register handlers as singletons in `Program.cs`.

## Unit test conventions

- **Framework:** xUnit + NSubstitute. Global usings: `using NSubstitute;` and `using Xunit;`.
- **Naming:** `MethodUnderTest_Scenario_ExpectedBehavior` (e.g., `DeleteAsync_InvokesStateChanged_WhenRepositoryReturnsTrue`).
- **Structure:** Dependencies created with `Substitute.For<IInterface>()` in the test class constructor or field initializers; `NullLogger<T>.Instance` for loggers.
- **Events:** Test `StateChanged` by attaching a counter lambda: `service.StateChanged += (_, _) => count++;`.
- Mirror the source project layout — tests for `src/KHost.Domain/Services/Foo.cs` go in `tests/KHost.UnitTests/Domain/Services/FooTests.cs`.

## Gotchas

- SCSS-only changes: run `npm run sass`, do not rebuild the .NET project.
- `src/KHost.Domain/Services/` — plural. Path `Servies` does not exist.
- `CompileSCSS` build target fails if `node_modules` is missing — run `npm install` first.
- Cache DB lives at `src/KHost.UserInterface/bin/Debug/net10.0/cache/` at runtime.
- All domain services are singletons — guard mutable state with `SemaphoreSlim`.
- `BlazorDisableThrowNavigationException` is set; navigation failures won't throw.
- `DefaultContext` tracking behaviour is set to `QueryTrackingBehavior.NoTracking`, so Saves/Updates will not update related models unless explicitly programmed to.
- **Migration reset**: any time a model that has a `DbSet<T>` in `DefaultContext` changes, delete all files in `src/KHost.DataAccess/Migrations/`, delete the runtime DB at `src/KHost.UserInterface/bin/Debug/net10.0/cache/khost.db`, then run `dotnet ef migrations add InitialSchema --project src/KHost.DataAccess`. **Then recreate the `AddMediaFts` migration** — the `media_fts` FTS5 virtual table and its sync triggers are raw SQL (not part of the EF model), so `dotnet ef` will NOT regenerate them and `MediaRepository` search will throw `no such table: media_fts`. Run `dotnet ef migrations add AddMediaFts --project src/KHost.DataAccess --startup-project src/KHost.UserInterface` and copy the `Up`/`Down` SQL from a prior `AddMediaFts.cs`.
