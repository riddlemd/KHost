# KHost

Open-source karaoke hosting software built on **.NET 10** and **.NET Aspire**. KHost pairs a Blazor Server "host console" for managing singers, songs, and playback with a separate Avalonia desktop "screen" app that renders karaoke video and audio to a second display.

> Repository: <https://github.com/riddlemd/KHost>

---

## Table of Contents

- [What is KHost?](#what-is-khost)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Architecture at a Glance](#architecture-at-a-glance)
- [Project Layout](#project-layout)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Configuration](#configuration)
- [Testing](#testing)
- [Learn More](#learn-more)
- [License](#license)

---

## What is KHost?

KHost is a two-part application for running a karaoke night:

1. **The host console** (`KHost.UserInterface`) — a Blazor Server web app used by the host (KJ) to queue up singers, search the song library, edit performer and venue info, and control playback in real time.
2. **The screen app** (`KHost.Screen`) — an Avalonia desktop application that renders the karaoke video output (typically on a projector or second monitor). It decodes video frames with FFmpeg and plays audio through OpenAL.

Both are orchestrated for local development by `KHost.AppHost`, a [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) app host that wires up service discovery, OpenTelemetry, logging, and HTTP resilience out of the box.

---

## Features

- **Singer queue management** — add, remove, and reorder singers; supports keyboard shortcuts and an optional "move to bottom after performance" rule.
- **Song / media search** — search the local library and queue a song for the currently selected singer.
- **Now-playing panel with playback controls** — play, stop, and track position.
- **Selected singer info** — view and edit the currently highlighted performer.
- **Library managers** — dedicated Settings pages for Media, Singers, and Venues.
- **Bulk media editor** — multi-select songs in the Media Manager to bulk edit artist names or swap title/artist, and bulk delete with confirmation.
- **Dialogs** — edit dialogs for singers/venues/media, performance history viewer, confirmation dialogs, and a settings menu.
- **Themeable UI** — CSS themes served via `/api/themes` and bootstrapped by an `IThemeService`.
- **Persistent song library** — SQLite via Entity Framework Core.
- **Durable queue state** — JSON-backed cache on disk (via `JsonFileCacheService`), so the queue survives restarts.
- **Second-screen karaoke output** — FFmpeg-decoded BGRA video frames + OpenAL audio, rendered by Avalonia.
- **Host ⇄ screen IPC over SignalR** — the host console hosts a SignalR hub at `/ipc/screen`; the Avalonia screen app connects back as a client and is driven remotely (load media, play/pause/stop-with-fade, seek, volume, pitch) while streaming its playback state back to the host. The **Screens dialog** lists connected screens and can launch a local screen process on demand.
- **First-class observability** — Serilog (console + daily-rolling file, 7-day retention) and OpenTelemetry with OTLP export, plus a dedicated `KHost.Telemetry` layer exposing custom KHost metrics (media parse/search/import/cache durations, queue mutations, playback state transitions) and trace activities through an `IAnalyticsService` / `IAnalyticsActivity` abstraction.
- **First-run setup wizard** — 3-step wizard at `/setup` that walks through creating an admin user, configuring the first venue, and importing an initial media library. Auto-skips completed steps on reload.
- **User accounts with role-based access** — user management with named groups (`Admin`, `Regular`, `Tipper`), granular permissions (`AddToQueue`, `ReorderQueue`, `ImportLibrary`, etc.), and Argon2id password hashing via `LocalAuthProvider`.
- **Tip tracking** — record tips per singer with amount, payment method, and notes; paginated tips manager in Settings; per-singer tip totals shown in the singers manager.
- **Lyrics lookup** — `ShowLyricsDialog` fetches plain-text lyrics from [LRCLIB.NET](https://lrclib.net) for any song in the queue.

---

## Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Runtime | .NET | `net10.0` |
| Orchestration | .NET Aspire AppHost SDK | `13.1.0` |
| Web UI | ASP.NET Core / Blazor Server (Interactive Server) | built-in to net10.0 |
| Real-time IPC | `Microsoft.AspNetCore.SignalR.Client` (host ⇄ screen) | `8.0.0` |
| Desktop UI | Avalonia (`Desktop`, `Themes.Fluent`, `Fonts.Inter`) | `12.0.1` |
| ORM | Entity Framework Core | `10.0.7` |
| Database | SQLite (`Microsoft.EntityFrameworkCore.Sqlite`) | `10.0.7` |
| Audio | `Silk.NET.OpenAL.Soft.Native` | `1.23.1` |
| Media metadata | `TagLibSharp` | `2.3.0` |
| FFmpeg integration | [`FFMpegCore`](https://github.com/rosenbjerg/FFMpegCore) | `5.4.0` |
| Video decode | FFmpeg (invoked as a child process) | external binary |
| Password hashing | `Konscious.Security.Cryptography.Argon2` | `1.3.1` |
| Logging | Serilog + `Serilog.Sinks.File` | `4.3.0` / `7.0.0` |
| Telemetry | OpenTelemetry + OTLP exporter | `1.15.3` |
| HTTP resilience | `Microsoft.Extensions.Http.Resilience` | `10.5.0` |
| Service discovery | `Microsoft.Extensions.ServiceDiscovery` | `10.5.0` |
| Styling | Sass (SCSS) | `1.69.5` |
| Testing | xUnit / NSubstitute / coverlet | `2.9.3` / `5.3.0` / `6.0.4` |

---

## Architecture at a Glance

KHost follows a layered architecture. Dependencies only point inward:

```
UI (KHost.UserInterface, KHost.Screen)
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
  KHost.Screen (Avalonia)  ── FFmpeg (video) / Silk.NET OpenAL (audio) ──► second display
```

> **Host ⇄ screen interop.** The UI hosts a SignalR hub (`KHost.IPC.SignalR`) at `/ipc/screen`. The Avalonia screen app connects back as a SignalR client (`--server-uri` / `--screen-id`), receives playback commands, and pushes its current `ScreenPlaybackState` back to the host. The host's `LocalScreenProvider` (an `IScreenProvider`) can spawn the screen process locally from the Screens dialog. So while `KHost.Screen` is a separate executable, it is no longer isolated — it is remote-controlled by the host console.

> **Why two persistence strategies?** The song library is large, relational, and benefits from SQL indexes on `FilePath`, `Title`, `Artist`, `Status`, and `DateAdded`. The queue and venue selection are small, frequently-mutated bits of "session" state; serializing them as JSON blobs is simpler and keeps the host running even if the DB is momentarily unavailable.

---

## Project Layout

The solution uses the newer `.slnx` (XML) format — open `KHost.slnx`, not a `.sln` file.

| Project | Role |
|---|---|
| `KHost.AppHost` | .NET Aspire orchestrator. Primary local dev entry point. Launches `KHost.UserInterface` as an Aspire resource. |
| `KHost.ServiceDefaults` | Shared Aspire defaults — OpenTelemetry, HTTP resilience, service discovery. Consumed via `builder.AddServiceDefaults()`. |
| `KHost.Abstractions` | All interfaces and abstraction-layer models. No project references. |
| `KHost.Domain` | Business logic, concrete models, and services (queue, playback, venues, singers, media, media search, metadata parsing, cache). Uses `TagLibSharp`. |
| `KHost.LrcLib` | Standalone HTTP client library for the [LRCLIB.NET](https://lrclib.net) lyrics API. No project references; consumed by `KHost.Domain` via `AddLrcLib()`. |
| `KHost.IPC.SignalR` | SignalR-based host ⇄ screen IPC: `ScreenHub`, `ScreenServerService` (`IScreenServer`), and `ScreenClient` (`IScreenClient`). Registered via `AddSignalRIPCServer()` + `MapIPCServer()` (host) and `AddSignalRIPCClient()` / `CreateScreenClient()` (screen). |
| `KHost.Telemetry` | OpenTelemetry metrics and trace activities (`KHostMetrics`, `KHostActivitySource`) plus the `IAnalyticsService` / `IAnalyticsActivity` implementation. Registered via `AddTelemetry()`. |
| `KHost.DataAccess` | EF Core 10 + SQLite persistence for the song library. |
| `KHost.UserInterface` | Blazor Server app — the host console. Razor components live under `Components/`. Hosts the IPC hub at `/ipc/screen` and exposes `/api/themes`. |
| `KHost.Screen` | Avalonia desktop app (WinExe on Windows, Exe elsewhere) for karaoke video/audio output. Custom FFmpeg + OpenAL wrappers. References `KHost.Abstractions`, `KHost.Telemetry`, and `KHost.IPC.SignalR`; connects to the host hub as a SignalR client (`--server-uri` / `--screen-id`). |
| `KHost.UnitTests` | xUnit + NSubstitute tests covering domain services. |
| `KHost.IntegrationTests` | xUnit integration test skeleton (no tests yet). |

---

## Prerequisites

Install these before your first build:

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** — required by every project.
- **[Node.js 18+](https://nodejs.org/)** and npm — the UI compiles SCSS with Sass as part of the build.
- **[FFmpeg](https://ffmpeg.org/download.html)** available on your `PATH` — required at runtime by `KHost.Screen` for video decoding. You can also point to a custom install via the `FFMPEG_PATH` environment variable.
- **Cross-platform** — `KHost.Screen` builds and runs on Windows, Linux, and macOS. On Windows it targets the `WinExe` subsystem (no console window) and enables COM interop for Avalonia; on Linux/macOS it builds as a plain `Exe`. OpenAL is loaded from the bundled `Silk.NET.OpenAL.Soft.Native` package when available and falls back to the system library (`soft_oal`/`openal32` on Windows, `libopenal` on Linux/macOS).
- (Optional) **[Docker Desktop](https://www.docker.com/products/docker-desktop/)** — if you want the full Aspire dashboard experience for local telemetry.

---

## Getting Started

```bash
# 1. Clone
git clone https://github.com/riddlemd/KHost.git
cd KHost

# 2. Restore .NET dependencies for the whole solution
dotnet restore KHost.slnx

# 3. Install the UI's npm packages (required by the SCSS build step)
cd KHost.UserInterface
npm install
cd ..

# 4. Run with Aspire (primary dev entry point)
dotnet run --project KHost.AppHost
```

Alternative entry points:

```bash
# Run the Blazor UI directly (no Aspire)
dotnet run --project KHost.UserInterface

# Run the Avalonia screen app (normally launched for you from the host's Screens dialog,
# which injects the host's live listening address as --server-uri).
# When run manually, point --server-uri at the URL the host is actually listening on
# (e.g. http://localhost:5251/ipc/screen for the UI's default http profile).
# --screen-id defaults to the machine name.
dotnet run --project KHost.Screen -- --server-uri http://localhost:5251/ipc/screen --screen-id main

# Build the whole solution
dotnet build KHost.slnx
```

> **First build failing on SCSS?** The `KHost.UserInterface` project has a `CompileSCSS` MSBuild target that runs `npm run sass` before every build. If `node_modules` is missing, the build fails fast — run `npm install` inside `KHost.UserInterface/` and retry.

---

## Development Workflow

### Hot reload (C# + SCSS together)

```bash
cd KHost.UserInterface
npm run dev
```

This runs `dotnet watch` and `sass --watch` concurrently, so both Razor/C# edits and SCSS edits update live.

### SCSS-only changes

You don't need to rebuild the .NET project for a pure style change — just recompile the stylesheets:

```bash
cd KHost.UserInterface
npm run sass          # one-shot
npm run sass:watch    # continuous
```

### Code style conventions

- Interfaces live in **`KHost.Abstractions`**, concrete types in their implementation project.
- Services that expose configuration use the `ServiceOptions` nested-class pattern with `BindConfiguration(ServiceOptions.SectionName)` and `IOptionsMonitor<ServiceOptions>` for live reload.
- Disposable services follow the standard `Dispose()` / `protected virtual Dispose(bool disposing)` pattern.
- CSS: top-level class names are prefixed `kh-`, buttons are prefixed `btn-`, [BEM](https://getbem.com/introduction/) naming is used. No inline `style` attributes, no `<style>` blocks — all styles go in `.scss` files.
- Events use `EventHandler`-style delegates, not `event Action`/`event Func`.

---

## Configuration

`KHost.UserInterface/appsettings.json` exposes these sections:

| Section | Purpose |
|---|---|
| `Logging` | Standard `Microsoft.Extensions.Logging` levels. Serilog is layered on top in `Program.cs`. |
| `AllowedHosts` | ASP.NET Core host filter (defaults to `*`). |
| `Audio.Volume` | Master audio volume (`0.0`–`1.0`). |
| `PlaybackService.MoveSingerToBottomAfterPerformance` | When true, moves the just-performed singer to the bottom of the queue. |
| `SingerQueueService.PromptBeforeRemovingSinger` | Confirmation prompt when removing a singer. |
| `SingerQueueService.ClearOnClose` | Clears the queue when the app shuts down. |
| `LocalScreen.ExePath` | Optional override for the `KHost.Screen` executable path. Defaults to `KHost.Screen.exe` next to the host binary. |
| `LocalScreen.ServerUri` | SignalR hub URI passed to a launched screen process. When unset, the host injects its own live listening address at startup (handles dynamic/Aspire-assigned ports); set this only to force a specific URI. |
| `FFmpegPath` | Optional path to the FFmpeg binary directory when it isn't on `PATH`. |
| `MediaFileParsingService.*` | Filename-to-metadata parsing rules: `Format` (artist-first / title-first), `Separators`, `PrefixStripPatterns`, `TitleNoisePatterns`, `FeaturingPattern`, `FeaturingHandling`, and `FallbackArtistName`. |

Environment-specific overrides live in `appsettings.Development.json`.

Runtime files you'll see on disk:

- **`./cache/*.json`** — queue and venue state files written by `JsonFileCacheService` (relative to the UI process's working directory).
- **`./logs/*.log`** — Serilog daily-rolling files; logs older than 7 days are pruned at startup.
- **The SQLite file** — managed by `IDatabaseInitializer`, which runs `InitializeAsync()` at startup.

---

## Testing

Unit tests use **xUnit** and **NSubstitute**:

```bash
# Just the unit tests
dotnet test KHost.UnitTests

# Everything in the solution
dotnet test KHost.slnx
```

`KHost.IntegrationTests` is a project skeleton — it currently has no tests or project references. New integration tests belong there.

---

## Learn More

New to any of the technologies KHost uses? These resources are a good starting point.

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

### Avalonia (the screen app)
- [Avalonia documentation](https://docs.avaloniaui.net/)
- [Avalonia tutorial](https://docs.avaloniaui.net/docs/tutorials/todo-list-app/)
- [Avalonia samples](https://github.com/AvaloniaUI/Avalonia.Samples)

### Entity Framework Core + SQLite
- [EF Core documentation](https://learn.microsoft.com/ef/core/)
- [EF Core with SQLite](https://learn.microsoft.com/ef/core/providers/sqlite/)
- [SQLite documentation](https://www.sqlite.org/docs.html)

### Media playback
- [FFmpeg documentation](https://ffmpeg.org/documentation.html) / [FFmpeg wiki](https://trac.ffmpeg.org/wiki)
- [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) (the .NET wrapper KHost uses to probe and invoke ffmpeg)
- [OpenAL Soft](https://openal-soft.org/)
- [Silk.NET](https://dotnet.github.io/Silk.NET/) (cross-platform bindings for OpenAL, etc.)
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

---

## TODO

Brainstorm of features a full-featured karaoke hosting application should support. Useful as a backlog/roadmap reference. Grouped by functional area.

### Singer Queue Management
| Feature | Priority | Notes |
|---|---|---|
| ~~Drag-and-drop reorder of the queue~~ | Low | Done |
| Fair rotation algorithm (round-robin by singer, not by song) | Medium | |
| VIP / priority slots that jump the rotation | Low | |
| Restore a previously skipped singer back into rotation (Some kind of out of placeholder?) | Low | |
| Duet / group performance support (multiple names on one slot) | Low | |
| Mark a singer as "on deck" / warming up | Low | |
| Auto-remove singers who've been absent for X turns | Low | |

### Song Library & Search
| Feature | Priority | Notes |
|---|---|---|
| Search by title with fuzzy / typo-tolerant matching | High | SQLite FTS5 with BM25 ranking in place; fuzzy/typo-tolerant layer not yet added |
| ~~Search by artist~~ | High | Done |
| ~~Multi-folder~~ / multi-drive library sources | High | Done |
| ~~Background library scan with progress and cancel~~ | High | Done |
| Search by genre, decade, or language | Low | |
| ~~Bulk metadata editor (artist, title, album, year)~~ | Medium | Done — artist field and title/artist swap; can extend to more fields |

### Playback Engine
| Feature | Priority | Notes |
|---|---|---|
| Pitch / key adjustment (±N semitones) without tempo change | High | Implemented in `DefaultMediaPlayer` via FFmpeg `asetrate+aresample+atempo` filters; no host-console UI control yet |
| Audio device selection per output (mains vs. headphone cue) | Low | |
| Volume control with smooth fade in/out | Medium | Fade-out (`FadeOutAsync`) implemented in `DefaultMediaPlayer`; volume is a startup config value (`Audio.Volume`); no runtime slider |
| Tempo adjustment without pitch change | Low | |
| ~~Wide format support: CDG+MP3, MP4, MKV, AVI, WebM, WMV~~ | Medium | Done |
| Mid-song cut ("kill song") with graceful fade | Medium | `FadeOutAsync` with configurable step/duration in `DefaultMediaPlayer`; not yet triggerable from the host console |
| Per-song saved key / tempo overrides remembered next time | Medium | |
| Crossfade or hard cut between songs | Low | |
| Vocal removal / karaoke-mode toggle for source tracks with vocals | Low | |

### Display / Screen Output
| Feature | Priority | Notes |
|---|---|---|
| ~~Remote-controlled screen output from the host console~~ | High | Done — host ⇄ screen IPC over SignalR (`KHost.IPC.SignalR`); load/play/pause/stop/seek/volume/pitch commands and state feedback |
| Multi-monitor support with independent output config | Low | IPC supports multiple screens by `screen-id`; per-screen output config not yet built |
| True fullscreen video output (no taskbar / chrome) | High | |
| Scrolling marquee with next-up singers | Medium | |
| Idle/attract loop with background video, slides, or playlist | Medium | |
| Big "Up Next: <Name>" announcement card before each song | Medium | |
| Resolution / aspect-ratio scaling for any display | Medium | |
| Custom branding / logo / watermark overlay | Low | |
| Promotional / sponsor slides rotated between songs | Low | |
| Birthday / anniversary / special-occasion shoutouts | Low | |
| Safe-area guides for projectors and TVs | Low | |

### Singer-Facing (Mobile)
| Feature | Priority | Notes |
|---|---|---|
| QR code on screen so singers can join from their phone | Low | Requires Online Service |
| Mobile web app for browsing the library | Low | Requires Online Service |
| Self-serve add-to-queue from phone | Low | Requires Online Service |
| See own queue position in real time | Low | Requires Online Service |
| Estimated wait time | Low | Requires Online Service |
| Push / browser notification when "you're up next" | Low | Requires Online Service |
| Optional singer accounts with persistent history | Low | Requires Online Service |
| Per-singer favorites and "my songs" list | Low | Requires Online Service |
| Request a song that's not in the library | Low | Requires Online Service |
| Tip the KJ from the phone | Low | Requires Online Service |

### Host / KJ Tools
| Feature | Priority | Notes |
|---|---|---|
| KJ admin login / lock-screen | Medium | Auth service, provider, and Argon2 hasher implemented; login UI page not yet wired |
| ~~Multiple host accounts~~ | Low | Done |
| ~~Tip tracking per singer / per night~~ | Low | TipsService, TipsManagerPage, and per-singer totals in UsersManager fully implemented |

### Venue / Show Management
| Feature | Priority | Notes |
|---|---|---|
| Auto-save show state every N seconds; crash recovery | Medium | Queue and venues written to JSON on every mutation; periodic time-based snapshots not yet added |
| ~~Multiple venue profiles with distinct settings~~ | Medium | Done |
| Export show recap (songs played, singers, durations) | Low | |
| Per-venue rotation, cooldown, and branding rules | Low | Partial -- started moving configs to venues |
| Email or print end-of-night summary | Low | |
| Multi-show historical stats per venue | Low | |

### Reporting & Analytics
| Feature | Priority | Notes |
|---|---|---|
| Songs played per session and all-time | Medium | `PerformanceService` stores records in the DB; analytics/reporting UI not yet built |
| Most-requested songs and trending songs | Low | Requires Online Service |
| Most-active singers and new-singer count | Low | |
| Peak-hour analysis across nights | Low | |
| Per-genre and per-decade play distribution | Low | |

### Integrations & External Sources
| Feature | Priority | Notes |
|---|---|---|
| ~~Lyrics lookup via LRCLIB.NET~~ | Low | `KHost.LrcLib` + `ShowLyricsDialog` |
| YouTube / online karaoke source fetch with caching | Low | |
