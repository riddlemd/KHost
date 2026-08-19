# Developing KHost

Everything contributor-facing: architecture, project layout, workflow, and testing. For what KHost is and how to run it, see the [README](README.md).

## Table of Contents

- [Architecture](#architecture)
- [Project Layout](#project-layout)
- [Prerequisites](#prerequisites)
- [Running for Development](#running-for-development)
- [Development Workflow](#development-workflow)
- [Testing](#testing)
- [Learn More](#learn-more)

## Architecture

KHost follows a layered architecture. Dependencies only point inward:

```
UI (KHost.UserInterface, KHost.Screen2)
        │
        ▼
Domain  ──►  DataAccess
        │         │
        ▼         ▼
          Abstractions
```

- **`KHost.Abstractions`** is the innermost project — it defines *every* interface and has no project references. Both `Domain` and `DataAccess` reference it; the UI projects reference all three.
- **`KHost.Domain`** owns business logic and stateful services registered as singletons in DI. Stateful services (queue, venues, playback) raise a `StateChanged` event so Blazor components can call `StateHasChanged()` and re-render.
- **`KHost.DataAccess`** owns EF Core persistence for the song library.

Data flow at runtime:

```
  Browser (Blazor circuit)
        │
        ▼
  KHost.UserInterface ── Domain services ──► EF Core / SQLite   (song library)
        │     ▲                           └─► JsonFileCacheService (./cache/*.json)
        │     │                                                    (queue, venues)
        │     │  SignalR hub  /ipc/screen
   commands  state
        ▼     │
  KHost.Screen2 (Photino)  ── HLS stream served by the host ──► second display
```

> **Host ⇄ screen interop.** The UI hosts a SignalR hub (`KHost.IPC.SignalR`) at `/ipc/screen`. The screen app connects as a SignalR client (`--server-uri` / `--screen-id`), receives playback commands, and pushes its `ScreenPlaybackState` back to the host. The host's `LocalScreenProvider` (an `IScreenProvider`) spawns screen processes from the Screens dialog, so a screen is a separate executable that is remote-controlled by the host console.

> **Why the host transcodes.** The host runs one ffmpeg per song and serves the result as an HLS
> playlist under `/media/{session}/`. Screens fetch that stream instead of decoding the file, which
> is what lets several of them share a decode, keeps them on a common timeline, and makes it
> possible to hand the same URL to a Cast receiver that has no access to the library at all.

> **Reachable, without exposing the console.** Kestrel binds every interface so a receiver or an
> off-machine screen can fetch the stream, and each screen is handed the host address it actually
> connected on rather than one the host guesses. Since the UI has no authentication, `LanAccessPolicy`
> answers only `/media` and `/ipc/screen` off-box and 404s everything else — the queue, the library
> and venue settings stay on the host machine. The port is declared once, in `appsettings.json`
> (`Urls`); the launch profile intentionally sets no `applicationUrl`.

> **Why two persistence strategies?** The song library is large, relational, and benefits from SQL indexes on `FilePath`, `Title`, `Artist`, `Status`, and `DateAdded`. The queue and venue selection are small, frequently-mutated bits of "session" state; serializing them as JSON blobs is simpler and keeps the host running even if the DB is momentarily unavailable.

## Project Layout

The solution uses the `.slnx` (XML) format — open `KHost.slnx`; there is no `.sln`. Application projects live under `src/` and test projects under `tests/`.

### `src/`

| Project | Role |
|---|---|
| `KHost.AppHost` | .NET Aspire orchestrator. Launches `KHost.UserInterface` as an Aspire resource with the dashboard. |
| `KHost.ServiceDefaults` | Shared Aspire defaults — OpenTelemetry, HTTP resilience, service discovery. Consumed via `builder.AddServiceDefaults()`. |
| `KHost.Abstractions` | All interfaces and abstraction-layer models. No project references. |
| `KHost.Domain` | Business logic, concrete models, and services (queue, playback, venues, singers, media, media search, metadata parsing, cache). Uses `TagLibSharp`. |
| `KHost.LrcLib` | Standalone HTTP client library for the [LRCLIB.NET](https://lrclib.net) lyrics API. No project references; consumed by `KHost.Domain` via `AddLrcLib()`. |
| `KHost.IPC.SignalR` | SignalR-based host ⇄ screen IPC: `ScreenHub`, `ScreenServerService` (`IScreenServer`), and `ScreenClient` (`IScreenClient`). Registered via `AddSignalRIPCServer()` + `MapIPCServer()` (host) and `AddSignalRIPCClient()` / `CreateScreenClient()` (screen). |
| `KHost.Telemetry` | OpenTelemetry metrics and trace activities (`KHostMetrics`, `KHostActivitySource`) plus the `IAnalyticsService` / `IAnalyticsActivity` implementation. Registered via `AddTelemetry()`. |
| `KHost.Cast` | Chromecast sender built on Sharpcaster — discovery, connection and transport for a single receiver at a time. Deliberately separate from the screen abstractions. Registered via `AddCast()`. |
| `KHost.Plugins.Sdk` | Contracts a drop-in plugin implements; loaded from `src/Plugins`. |
| `KHost.DataAccess` | EF Core 10 + SQLite persistence for the song library. |
| `KHost.UserInterface` | Blazor Server app — the host console. Razor components live under `Components/`. Hosts the IPC hub at `/ipc/screen` and exposes `/api/themes`. |
| `KHost.Screen2` | Photino desktop app for karaoke video/audio output. Plays an HLS stream the host transcodes. References `KHost.Abstractions`, `KHost.Telemetry`, and `KHost.IPC.SignalR`; connects to the host hub as a SignalR client. |

### `tests/`

| Project | Role |
|---|---|
| `KHost.UnitTests` | xUnit + NSubstitute tests. Repositories run against a real in-memory SQLite database (`SqliteTestDatabase`). |
| `KHost.IntegrationTests` | Skeleton — new integration tests belong here. |

## Prerequisites

Everything the [README lists](README.md#prerequisites) — the .NET 10 SDK, Node.js 18+ with npm, and FFmpeg on `PATH` — plus:

- (Optional) **[Docker Desktop](https://www.docker.com/products/docker-desktop/)** — for the Aspire dashboard's local telemetry when running via `KHost.AppHost`.
- (Optional) **ffprobe** — ships with FFmpeg; the transcode tests use it to assert a segment really carries audio.
- (Optional) **[Chromecast-Emulator](https://github.com/riddlemd/Chromecast-Emulator)** listening on `127.0.0.1:8009` — the Cast tests skip without it.

## Running for Development

```bash
# With the Aspire dashboard (telemetry, resource view)
dotnet run --project src/KHost.AppHost

# The Blazor UI directly
dotnet run --project src/KHost.UserInterface

# Headless (no native window; serve the console to a browser)
dotnet run --project src/KHost.UserInterface -- --headless
```

Screens are normally launched from the host's Screens dialog, which injects the host's live
listening address as `--server-uri`. To run one by hand:

```bash
# --screen-id defaults to the machine name.
dotnet run --project src/KHost.Screen2 -- --server-uri http://localhost:5251/ipc/screen --screen-id main

# --log-level debug adds the state the page reports each tick (position, expected position,
# readyState), which is how you tell a screen that is behind from one that never started.
dotnet run --project src/KHost.Screen2 -- --server-uri http://localhost:5251/ipc/screen --log-level debug
```

Screen logs land in `logs/` beside the screen executable, one file per screen id per day.

## Development Workflow

### Hot reload

```bash
cd src/KHost.UserInterface
npm run dev           # runs dotnet watch
```

### SCSS

SCSS compiles inside `dotnet build` (AspNetCore.SassCompiler) — there is no separate sass step.
Component styles live beside their component (`Foo.razor.scss` → scoped `Foo.razor.css`; the
generated `.css` is gitignored — never edit or commit it). Shared blocks live under
`wwwroot/scss` and are pulled in via `app.scss`.

Building from the CLI while Visual Studio has the solution open? Redirect the output so the IDE's
`bin/` isn't locked:

```bash
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"
```

### Code style conventions

- Interfaces live in **`KHost.Abstractions`**, concrete types in their implementation project.
- Services that expose configuration use the `ServiceOptions` nested-class pattern with `BindConfiguration(ServiceOptions.SectionName)` and `IOptionsMonitor<ServiceOptions>` for live reload.
- Disposable services follow the standard `Dispose()` / `protected virtual Dispose(bool disposing)` pattern.
- CSS: top-level class names are prefixed `kh-`, [BEM](https://getbem.com/introduction/) naming is used. No inline `style` attributes, no `<style>` blocks — all styles go in `.scss` files.
- Events use `EventHandler`-style delegates, not `event Action`/`event Func`.

See [AGENTS.md](AGENTS.md) for the full conventions, including the gotchas.

## Testing

Unit tests use **xUnit** and **NSubstitute**:

```bash
# Just the unit tests
dotnet test tests/KHost.UnitTests

# Everything in the solution
dotnet test KHost.slnx
```

Two suites drive real external tools and guard their own presence:

- The transcode tests run real **ffmpeg**; the repository tests run against real in-memory **SQLite**.
- If ffmpeg/ffprobe are missing, `EnvironmentCoverageTests` fails the run in plain words rather than
  letting the skipped coverage read as a pass. Set `KHOST_SKIP_ENVIRONMENT_TESTS=1` to accept the gap.
- The Cast tests need the [Chromecast-Emulator](https://github.com/riddlemd/Chromecast-Emulator) listening on `127.0.0.1:8009` and skip without it.

## Learn More

New to any of the technologies KHost uses? These are good starting points.

### .NET & C#
- [.NET documentation](https://learn.microsoft.com/dotnet/)
- [C# language guide](https://learn.microsoft.com/dotnet/csharp/)
- [What's new in .NET 10](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview)

### .NET Aspire (orchestration)
- [Aspire overview](https://learn.microsoft.com/dotnet/aspire/get-started/aspire-overview)
- [Aspire AppHost](https://learn.microsoft.com/dotnet/aspire/fundamentals/app-host-overview)
- [Aspire samples](https://github.com/dotnet/aspire-samples)

### Blazor Server (the host console UI)
- [Blazor documentation](https://learn.microsoft.com/aspnet/core/blazor/)
- [Blazor component lifecycle](https://learn.microsoft.com/aspnet/core/blazor/components/lifecycle)
- [Interactive render modes](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes)

### Photino (the screen app)
- [Photino documentation](https://www.tryphotino.io/docs)
- [Photino.NET](https://github.com/tryphotino/photino.NET)
- [HLS specification](https://datatracker.ietf.org/doc/html/rfc8216)

### Entity Framework Core + SQLite
- [EF Core documentation](https://learn.microsoft.com/ef/core/)
- [EF Core with SQLite](https://learn.microsoft.com/ef/core/providers/sqlite/)
- [SQLite documentation](https://www.sqlite.org/docs.html)

### Media playback
- [FFmpeg documentation](https://ffmpeg.org/documentation.html) / [FFmpeg wiki](https://trac.ffmpeg.org/wiki)
- [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) (the .NET wrapper KHost uses to probe and invoke ffmpeg)
- [TagLib#](https://github.com/mono/taglib-sharp) (audio metadata)

### Observability
- [Serilog](https://serilog.net/)
- [OpenTelemetry for .NET](https://opentelemetry.io/docs/languages/net/)
- [HTTP resilience in .NET](https://learn.microsoft.com/dotnet/core/resilience/http-resilience)

### Testing
- [xUnit](https://xunit.net/)
- [NSubstitute](https://nsubstitute.github.io/)
- [coverlet (code coverage)](https://github.com/coverlet-coverage/coverlet)

### Styling
- [Sass guide](https://sass-lang.com/guide/)
- [BEM naming convention](https://getbem.com/introduction/)
- [Bootstrap Icons](https://icons.getbootstrap.com/) (only icons from Bootstrap are used; no Bootstrap CSS/JS is included)
