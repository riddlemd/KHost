# CLAUDE.md

**KHost** — karaoke host app. .NET 10 + Blazor Server UI, Avalonia desktop screen app. Solution file: `KHost.slnx` (no `.sln`).

## Commands

```bash
dotnet run --project KHost.UserInterface   # run UI directly
dotnet run --project KHost.AppHost         # run via Aspire
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"  # build whole solution (redirects output to avoid locking VS's bin/ folder)
dotnet test KHost.UnitTests                # run unit tests
```

SCSS only — no full rebuild needed:
```bash
cd KHost.UserInterface && npm run sass     # one-shot compile
```

## Architecture

Dependency direction: **UI → Domain / DataAccess → Abstractions**. `KHost.Abstractions` has no project references.

| Project | Role |
|---|---|
| `KHost.Abstractions` | All interfaces and shared models. File-scoped namespaces. |
| `KHost.Domain` | Business logic, services, domain models. Registered via `AddDomain()`. |
| `KHost.DataAccess` | EF Core 10 + SQLite. `DefaultContext`, `BaseRepository<T>`. Registered via `AddDataAccess()`. |
| `KHost.UserInterface` | Blazor Server app. Interactive Server render mode. |
| `KHost.Screen` | Avalonia desktop app for video output. Self-contained, not wired to UI. |
| `KHost.UnitTests` | xUnit + NSubstitute. |

## Conventions

**Interfaces → `KHost.Abstractions/Services/` or `KHost.Abstractions/Models/`.** Concrete implementations go in `KHost.Domain/` or `KHost.DataAccess/`.

**DI registration** — each project exposes a `ProjectExtensions` method (`AddDomain()`, `AddDataAccess()`). All domain services are singletons.

**Repositories** — `BaseRepository<T>` takes `IDbContextFactory<DefaultContext>`. Always `using var context = await ContextFactory.CreateDbContextAsync();` per operation — never store a context.

**State persistence** — SQL (`DefaultContext`) for library/users/groups. JSON cache under `./cache/` (`ICacheService`) for queue and venue state.

**Events** — stateful services expose `StateChanged`; Blazor components subscribe in `OnInitialized` and call `StateHasChanged`.

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

## Gotchas

- SCSS-only changes: run `npm run sass`, do not rebuild the .NET project.
- `KHost.Domain/Services/` — plural. Path `Servies` does not exist.
- `CompileSCSS` build target fails if `node_modules` is missing — run `npm install` first.
- Cache DB lives at `KHost.UserInterface/bin/Debug/net10.0/cache/` at runtime.
- All domain services are singletons — guard mutable state with `SemaphoreSlim`.
- `BlazorDisableThrowNavigationException` is set; navigation failures won't throw.
- `DefaultContext` tracking behaviour is set to `QueryTrackingBehavior.NoTracking`, so Saves/Updates will not update related models unless explicitly programmed to.
- **Migration reset**: any time a model that has a `DbSet<T>` in `DefaultContext` changes, delete all files in `KHost.DataAccess/Migrations/`, delete the runtime DB at `KHost.UserInterface/bin/Debug/net10.0/cache/khost.db`, then run `dotnet ef migrations add InitialSchema --project KHost.DataAccess`.