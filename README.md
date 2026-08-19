# KHost

Open-source karaoke hosting software built on **.NET 10**. KHost pairs a Blazor Server "host console" for managing singers, songs, and playback with a lightweight desktop "screen" app that renders karaoke video and audio to a second display — or to a Chromecast.

KHost runs on Windows, Linux, and macOS.

## Table of Contents

- [What is KHost?](#what-is-khost)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Development](#development)
- [License](#license)

## What is KHost?

KHost is a two-part application for running a karaoke night:

1. **The host console** (`KHost.UserInterface`) — a Blazor Server web app used by the host (KJ) to queue up singers, search the song library, edit performer and venue info, and control playback in real time.
2. **The screen app** (`KHost.Screen2`) — a Photino desktop application that renders the karaoke video output (typically on a projector or second monitor). It plays an HLS stream the host transcodes, so it decodes nothing itself.

The host transcodes each song once and serves it as an HLS stream, so any number of screens — and a Chromecast receiver — can play the same song on a shared timeline.

## Features

- **Singer queue management** — add, remove, and reorder singers; supports keyboard shortcuts and an optional "move to bottom after performance" rule.
- **Song / media search** — search the local library and queue a song for the currently selected singer.
- **Now-playing panel with playback controls** — play, stop, and track position.
- **Second-screen karaoke output** — the host transcodes each song to HLS once and every screen plays that stream, so they share one decode and one timeline.
- **Multiple screens, one timeline** — the host anchors playback to a scheduled start and every screen corrects its own playhead towards it, seeking rather than trimming playback rate, so nothing is pitch-shifted to catch up.
- **Screen roles** — each screen can be muted or unmuted independently, have its picture blanked while it keeps running on the timeline, and one screen holds the *primary* role that the others are measured against. The Screens dialog shows live drift per screen.
- **Chromecast output** — receivers are discovered on demand from the Screens dialog and driven over CASTV2. A receiver is deliberately not a screen: it cannot hold the group timeline, only one is driven at a time, and the host connects out to it rather than the other way round.
- **Screens survive a lost host** — if the host goes away, a screen pauses and says so rather than playing on where nobody can stop it.
- **Errors a host can act on** — failures that a KJ can do something about are shown in plain words with what happened, what to try, and a reference code; the stack trace stays collapsed.
- **Library managers** — dedicated Settings pages for Media, Singers, and Venues, with a bulk media editor.
- **First-run setup wizard** — a 3-step wizard at `/setup` that walks through creating an admin user, configuring the first venue, and importing an initial media library.
- **User accounts with role-based access** — named groups, granular permissions, and Argon2id password hashing.
- **Tip tracking** — record tips per singer with amount, payment method, and notes; per-singer totals in the singers manager.
- **Lyrics lookup** — fetches plain-text lyrics from [LRCLIB.NET](https://lrclib.net) for any song in the queue.
- **Persistent song library** — SQLite via Entity Framework Core; queue and venue state survive restarts via a JSON-backed cache.
- **Themeable UI** — switchable CSS themes.
- **First-class observability** — Serilog and OpenTelemetry with OTLP export, plus custom KHost metrics and trace activities.

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Host console | ASP.NET Core / Blazor Server |
| Screen app | Photino.NET (platform web view) |
| Host ⇄ screen IPC | SignalR |
| Transcoding | FFmpeg (one process per song, served as HLS) |
| Chromecast | [Sharpcaster](https://github.com/Tapanila/SharpCaster) (CASTV2) |
| Database | SQLite via Entity Framework Core |
| Media metadata | TagLibSharp |
| Password hashing | Argon2id (`Konscious.Security.Cryptography.Argon2`) |
| Observability | Serilog, OpenTelemetry (OTLP) |
| Styling | Sass (SCSS), BEM naming, Bootstrap Icons |

Exact package versions live in [`Directory.Packages.props`](Directory.Packages.props).

## Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** — KHost runs from source, so the SDK builds it on first run.
- **[Node.js 18+](https://nodejs.org/)** and npm — the build stages vendor scripts from `node_modules`.
- **[FFmpeg](https://ffmpeg.org/download.html)** on your `PATH` — the host transcodes each song to HLS for the screens. A custom install can be pointed to via the `FFmpegPath` setting or the `FFMPEG_PATH` environment variable.

Tooling for working *on* KHost is listed in [DEVELOPMENT.md](DEVELOPMENT.md#prerequisites).

## Getting Started

```bash
# 1. Clone
git clone https://github.com/riddlemd/KHost.git
cd KHost

# 2. Restore .NET dependencies
dotnet restore KHost.slnx

# 3. Install the UI's npm packages (the build stages vendor scripts from node_modules)
cd src/KHost.UserInterface && npm install && cd ../..

# 4. Run
dotnet run --project src/KHost.UserInterface
```

The host opens in its own window and listens on `http://localhost:5251`. Launch screens from the **Screens dialog** in the toolbar — the host injects its own address into each screen it starts.

> **First build failing on `npm run copy:vendors`?** The build stages vendor scripts from `node_modules` and fails fast when it is missing — run `npm install` inside `src/KHost.UserInterface/` and retry.

For running with the Aspire dashboard, launching screens by hand, hot reload, and everything else contributor-facing, see [DEVELOPMENT.md](DEVELOPMENT.md).

## Configuration

`src/KHost.UserInterface/appsettings.json` exposes these sections:

| Section | Purpose |
|---|---|
| `Logging` | Standard `Microsoft.Extensions.Logging` levels. Serilog is layered on top in `Program.cs`. |
| `AllowedHosts` | ASP.NET Core host filter (defaults to `*`). |
| `Urls` | Where Kestrel listens; a semicolon-separated list. Defaults to `http://0.0.0.0:5251` so Cast receivers and off-machine screens can reach the stream. Only `/media` and `/ipc/screen` are answered off-box — the console itself stays on the host machine. |
| `Audio.Volume` | Master audio volume (`0.0`–`1.0`). |
| `PlaybackService.MoveSingerToBottomAfterPerformance` | When true, moves the just-performed singer to the bottom of the queue. |
| `SingerQueueService.PromptBeforeRemovingSinger` | Confirmation prompt when removing a singer. |
| `SingerQueueService.ClearOnClose` | Clears the queue when the app shuts down. |
| `LocalScreen.ExePath` | Optional override for the `KHost.Screen2` executable path. Defaults to `KHost.Screen2.exe` next to the host binary. |
| `LocalScreen.ServerUri` | SignalR hub URI passed to a launched screen process. When unset, the host injects its own live listening address at startup; set this only to force a specific URI. |
| `FFmpegPath` | Optional path to the FFmpeg binary directory when it isn't on `PATH`. |
| `MediaFileParsingService.*` | Filename-to-metadata parsing rules: `Format` (artist-first / title-first), `Separators`, `PrefixStripPatterns`, `TitleNoisePatterns`, `FeaturingPattern`, `FeaturingHandling`, and `FallbackArtistName`. |

Cast discovery is **not** configured here. It sweeps the whole network, so it is off at startup and turned on from the Screens dialog for as long as it is needed; closing the dialog stops it.

Environment-specific overrides live in `appsettings.Development.json`.

Runtime files on disk:

- **`./cache/*.json`** — queue and venue state; the queue survives restarts.
- **`./logs/*.log`** — daily-rolling logs; entries older than 7 days are pruned at startup.
- **The SQLite file** — the song library, created on first run.

## Development

Architecture, project layout, development workflow, testing, and the roadmap live in [DEVELOPMENT.md](DEVELOPMENT.md).

Contributions are accepted under the terms in [CONTRIBUTING.md](CONTRIBUTING.md).

## License

KHost is licensed under the [PolyForm Shield License 1.0.0](LICENSE).

You may use, modify, and self-host KHost for any purpose, **including commercial
use** (for example, running it to host your own karaoke events). You may **not**
use it to provide a product or service that competes with KHost — including
offering KHost or a derivative as a hosted/managed service (SaaS), or
redistributing it under a different brand — without a separate license.

**Commercial, SaaS, and OEM licenses are available** for those uses — contact
Michael Riddle <riddlemd@gmail.com>.

Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md),
with full license texts under [`licenses/`](licenses). FFmpeg is **not** distributed
with KHost; it is obtained by the user (see that file).
