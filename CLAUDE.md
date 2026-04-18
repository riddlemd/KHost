# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

**KHost** is a karaoke hosting application built on .NET 10 and .NET Aspire. It provides a Blazor Server-based web UI for managing a singer queue, searching a song library, and controlling playback, plus an Avalonia-based desktop "screen" app for rendering karaoke video output.

The solution uses the newer `.slnx` (XML) format (`KHost.slnx`) — there is no `.sln` file.

## Commands

All projects target `net10.0`.

### Build & Run

```bash
# Primary development entrypoint — Aspire orchestrator that brings up the UI
dotnet run --project KHost.AppHost

# Run the Blazor UI directly (no Aspire)
dotnet run --project KHost.UserInterface

# Run the Avalonia desktop "screen" app
dotnet run --project KHost.Screen

# Build the whole solution
dotnet build KHost.slnx
```

### UI Dev Workflow (SCSS + hot reload)

`KHost.UserInterface` depends on Sass for styling. SCSS is compiled to `wwwroot/css/` automatically before build via an MSBuild `CompileSCSS` target that runs `npm run sass`. Node dependencies must be installed before the first build.

```bash
cd KHost.UserInterface
npm install                 # once
npm run dev                 # runs `dotnet watch` + `sass --watch` concurrently
npm run sass                # one-shot SCSS compile (app + themes)
npm run sass:watch          # just watch SCSS
```

Do not try to rebuild the .net project if a change only affects .scss files, just run `npm run sass`

### Tests

The solution contains two test projects:

- **`KHost.UnitTests`** — xUnit + NSubstitute. References `Abstractions`, `DataAccess`, and `Domain`. Covers domain models, services, and collection extensions (e.g. `PlaybackServiceTests`, `SingerQueueServiceTests`, `VenueServiceTests`, `JsonFileCacheServiceTests`, `SongSearchServiceTests`, `ListExtensionsTests`).
- **`KHost.IntegrationTests`** — xUnit. Project exists but currently has no test files or project references wired up.

```bash
# Run unit tests
dotnet test KHost.UnitTests

# Run all tests in the solution
dotnet test KHost.slnx
```

## Architecture

The solution follows a layered architecture with strict dependency direction: **UI → Domain / DataAccess → Abstractions**. `KHost.Abstractions` has no project references and is the only project allowed to be referenced by every layer.

### Projects

| Project | Role | Key references |
|---|---|---|
| `KHost.AppHost` | .NET Aspire orchestrator (dev entrypoint). Just launches `KHost.UserInterface` as an Aspire resource. | `KHost.UserInterface` |
| `KHost.ServiceDefaults` | Shared Aspire defaults: OpenTelemetry, HTTP resilience, service discovery. Consumed via `builder.AddServiceDefaults()`. | — |
| `KHost.Abstractions` | **All interfaces live here.** Models (`ISong`, `ISinger`, `IVenue`, `IQueuedSong`, `ISongSearchEntity`, `IPaginatedResult`), services (`ICacheService`, `IPlaybackService`, `ISingerQueueService`, `ISongSearchService`, `IVenueService`, `IMediaFileParsingService`), and `IMediaPlayer`. Uses file-scoped namespaces. | — |
| `KHost.Domain` | Business logic, domain models (concrete `Song`, `Singer`, `Venue`, `QueuedSong`, `PaginatedResult`, `SongSearchEntity`), and services (`PlaybackService`, `SingerQueueService`, `SongSearchService`, `VenueService`, `MediaFileParsingService`, `JsonFileCacheService`). Uses `TagLibSharp` for media metadata. Services registered via `AddDomain()`. Also contains `Collections/Generics/ListExtensions.cs`. | `KHost.Abstractions` |
| `KHost.DataAccess` | EF Core 10 persistence. `SongLibraryContext` (DbContext) under `Contexts/`, plus `BaseRepository<T>` and `SongsRepository` under `Repositories/`. Registered via `AddDataAccess()`. | `KHost.Abstractions`, `KHost.Domain` |
| `KHost.UserInterface` | Blazor Server app — the primary UI. Interactive Server render mode. Razor components under `Components/Karaoke` (`AppStatusBar`, `NowPlayingPanel`, `SelectedSingerInfoPanel`, `SingerQueuePanel`, `SongSearchPanel`), `Components/Pages`, `Components/Layout`. Also exposes `/api/themes`. | DataAccess, Domain, Abstractions, ServiceDefaults |
| `KHost.Screen` | Avalonia desktop app (WinExe) for karaoke video output. Uses FFmpeg (custom wrappers in `FFmpeg/`: `FfmpegService`, `IFfmpegService`, `MediaInfoParser`, `AviDemuxer`, `FfmpegModels`) and OpenAL via `Silk.NET` (`OpenAl/`: `OpenAlAudioPlayer`, `OpenAlNative`). `DefaultMediaPlayer` implements `IMediaPlayer`. | `KHost.Abstractions` |
| `KHost.UnitTests` | xUnit + NSubstitute test project covering Domain models and services. | `Abstractions`, `DataAccess`, `Domain` |
| `KHost.IntegrationTests` | xUnit integration test project (skeleton — no tests yet). | — |
| `KHost.Common` | Currently empty placeholder (only `Class1.cs` stub). | — |

### Cross-cutting conventions

**Interfaces go in `KHost.Abstractions`.** Concrete types (models, services, repositories) live in their implementation project and implement the abstraction. When adding a new service, create the interface in `KHost.Abstractions/Services/` (file-scoped namespace style) and the implementation in `KHost.Domain/Services/` or the appropriate project.

**Service registration uses per-project `ProjectExtensions`.** Each implementation project exposes an `IServiceCollection` extension:
- `KHost.Domain.ProjectExtensions.AddDomain()` — binds `ServiceOptions` for `PlaybackService`, `SingerQueueService`, `VenueService`, and `JsonFileCacheService` to configuration, then registers all domain services as singletons: `ICacheService → JsonFileCacheService`, `ISingerQueueService → SingerQueueService`, `IPlaybackService → PlaybackService`, `ISongSearchService → SongSearchService`, `IVenueService → VenueService`.
- `KHost.DataAccess.ProjectExtensions.AddDataAccess()` — currently a no-op stub; `SongLibraryContext` and repositories are **not** yet registered here.

These are wired in `KHost.UserInterface/Program.cs`:
```csharp
builder.AddServiceDefaults();
builder.Services.AddDomain();
builder.Services.AddDataAccess();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

**Service options pattern.** Configurable services expose a nested `ServiceOptions` class with a `public const string SectionName = nameof(...)` and are bound via `.BindConfiguration(ServiceOptions.SectionName)`. Config lives in `KHost.UserInterface/appsettings.json` under sections like `PlaybackService`, `SingerQueueService`, `VenueService`, `JsonFileCacheService`, and `Audio`.

**State persistence is split.**
- **Song library** → SQL via `SongLibraryContext` (EF Core 10). The context maps `DbSet<ISong> Songs` with fluent configuration and indexes on `FilePath` (unique), `Title`, `Artist`, `Status`, and `DateAdded`.
- **Queue / venue state** → JSON files under `./cache/` via `ICacheService` (`JsonFileCacheService`). The cache service serializes with `JsonSerializerOptions.Web`, locks file I/O with a `SemaphoreSlim`, and resolves paths as `Path.Combine(CachePath, key + ".json")`. `SingerQueueService` uses key `"singer-queue"` and `VenueService` uses `"venues"`. Domain services call `_cacheService.LoadAsync<T>(key)` on startup and `SaveAsync` on mutation, then raise `StateChanged` events so Blazor components re-render.

**Repository pattern.** `BaseRepository<T>` is generic and takes `IDbContextFactory<SongLibraryContext>` (not a scoped `DbContext`), creating a fresh context per operation via `using var context = await ContextFactory.CreateDbContextAsync();`. This matches Blazor Server's long-lived circuit model. The base exposes `CreateAsync`, `ReadAsync`, `UpdateAsync`, `DeleteAsync`, `ReadAllAsync`, and a generic `SearchAsync<TOptions>` that returns `PaginatedResult<T>` (defaults: `DefaultPageSize = 50`, `MaxPageSize = 1000`). Derived repositories override the abstract `ApplySearchFilters<TOptions>` to plug in type-specific filtering. `SongsRepository` is the only current derivation.

**Event-driven state synchronization.** Stateful services (`ISingerQueueService`, `IVenueService`, `IPlaybackService`) expose a `StateChanged` event; Blazor components subscribe in `OnInitialized` and call `StateHasChanged` to re-render on mutation.

**Options monitoring.** Services inject `IOptionsMonitor<ServiceOptions>` rather than `IOptions<T>` so they pick up live configuration changes.

**Disposable services.** `IPlaybackService` and `IVenueService` both extend `IDisposable`. `PlaybackService` disposes an internal `Timer`; implementations should follow the standard `Dispose()` / `protected virtual Dispose(bool disposing)` pattern (see [General Principles](#general-principles) below).

**Interface segregation between Domain and Abstractions.** Domain services implement abstraction interfaces, but some methods have overloads: a public method typed to the concrete domain type (e.g., `SongSearchEntity`) and an explicit interface implementation that accepts the abstraction (`ISongSearchEntity`) and down-casts. See `SingerQueueService.AddSongAsync` for the pattern.

### KHost.Screen media stack

`KHost.Screen` is a self-contained media pipeline, not wired to the Blazor UI. `DefaultMediaPlayer` implements `IMediaPlayer` (defined in `KHost.Abstractions/MediaPlayer/IMediaPlayer.cs`) which emits decoded BGRA video frames on `FrameAvailable` and plays audio via OpenAL. Video decoding uses FFmpeg invoked as a child process (see `FFmpeg/FfmpegService.cs`, `MediaInfoParser.cs`, `AviDemuxer.cs`). Avalonia views live in `Views/`.

## General Principles

Organize class members in a logical, predictable order that improves readability and maintainability. Members should be grouped by type and visibility, with public members before internal/private ones.

## Recommended Order

### 1. **Fields** (Private, then Protected)
- Static fields first
- Instance fields second
- Use `private` by default, `protected` only when inheritance is needed

### 3. **Events**
- Public events first
- Internal/Protected events
- Private events

### 4. **Properties**
- Public properties (auto-properties first, then with backing fields)
- Internal/Protected properties
- Private properties
- Index properties/Indexers
- 
### 5. **Public Methods**
- Group logically related methods
- Overloads together
- Special methods last (operator overloads, implicit/explicit casts)

### 6. **Internal/Protected Methods**
- Organized same way as public methods

### 7. **Private Methods**
- Helper methods
- Implementation details

### 8. **Nested Types**
- Classes, interfaces, records, enums
- Public before internal/private

## Special Cases

### IDisposable Implementation
Place at the end of public methods, after all functional methods:

```csharp
public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}

protected virtual void Dispose(bool disposing)
{
    if (disposing)
    {
        _timer?.Dispose();
    }
}
```

### Explicit Interface Implementation
Place after public implementation of the same interface method:

```csharp
public void Load(ConcreteType item) { }

void IInterface.Load(IInterfaceType item)
{
    if (item is ConcreteType concrete)
        Load(concrete);
}
```

### Auto-Properties vs Properties with Backing Fields
Auto-properties should come before properties with backing fields:

```csharp
// Auto-properties first
public string Name { get; set; }
public int Count { get; set; }

// Properties with backing fields
private List<Item> _items = [];
public IReadOnlyList<Item> Items => _items.AsReadOnly();

private int _cachedValue;
public int CachedValue
{
    get => _cachedValue;
    private set => _cachedValue = value;
}
```

## Gotchas

- The Domain services folder is `KHost.Domain/Services/` (plural). Earlier commits/messages may reference `Servies` — that path does not exist.
- `KHost.UserInterface` has `BlazorDisableThrowNavigationException` set; navigation failures won't throw.
- The SCSS build target (`CompileSCSS` in `KHost.UserInterface.csproj`) fails the build if `node_modules` is missing — run `npm install` before building fresh clones.
- `KHost.DataAccess.AddDataAccess()` currently does not register the `SongLibraryContext` or any repository — DI wiring for data access is incomplete.
- `KHost.IntegrationTests` has no project references and no tests yet — the project is a skeleton.
- Cache files live at `KHost.UserInterface/cache/` at runtime (working directory of the UI process); a second empty `./cache/` directory exists at the repo root.
- All domain services are registered as **singletons**, so any mutable state they hold is shared across the entire Blazor Server process — take care with thread safety (the existing services guard I/O with `SemaphoreSlim`).
