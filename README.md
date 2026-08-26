# KHost

Open-source karaoke hosting software built on **.NET 10**. KHost pairs a Blazor Server "host console" for managing singers, songs, and playback with a lightweight desktop "screen" app that renders karaoke video and audio to a second display — or to a Chromecast.

The host transcodes each song once (FFmpeg → HLS) and serves the stream, so any number of screens — and a Chromecast receiver — play the same song on a shared timeline.

Between singers it fills the room from break music playlists, and runs ad rolls in the gap after a performance — never over one.

KHost runs on Windows, Linux, and macOS.

## Documentation

All documentation lives in the **[KHost wiki](https://github.com/riddlemd/KHost/wiki)**:

- [Getting Started](https://github.com/riddlemd/KHost/wiki/Getting-Started) — prerequisites, running from source, first-run setup
- [Configuration](https://github.com/riddlemd/KHost/wiki/Configuration) — settings reference
- [Architecture](https://github.com/riddlemd/KHost/wiki/Architecture) — how the pieces fit together
- [Break Music and Ads](https://github.com/riddlemd/KHost/wiki/Break-Music-and-Ads) — playlists, ad triggers, priority, volume
- [Plugins](https://github.com/riddlemd/KHost/wiki/Plugins) — extension points, installing from the catalog, publishing a plugin
- [Development](https://github.com/riddlemd/KHost/wiki/Development) and [Testing](https://github.com/riddlemd/KHost/wiki/Testing) — contributor workflow

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), [Node.js 18+](https://nodejs.org/), and [FFmpeg](https://ffmpeg.org/download.html) on `PATH`.

```bash
git clone https://github.com/riddlemd/KHost.git
cd KHost
dotnet restore KHost.slnx
cd src/KHost.UserInterface && npm install && cd ../..
dotnet run --project src/KHost.UserInterface
```

The host opens in its own window on `http://localhost:5251` and walks through first-run setup at `/setup`.

## Contributing

Contributions are accepted under the terms in [CONTRIBUTING.md](CONTRIBUTING.md). Coding conventions live in [AGENTS.md](AGENTS.md) and the [wiki](https://github.com/riddlemd/KHost/wiki/Development).

## License

KHost is licensed under the [PolyForm Shield License 1.0.0](LICENSE), **except
for the plugin SDK**, which is [MIT](src/KHost.Plugins.Sdk/LICENSE).

You may use, modify, and self-host KHost for any purpose, **including commercial
use** (for example, running it to host your own karaoke events). You may **not**
use it to provide a product or service that competes with KHost — including
offering KHost or a derivative as a hosted/managed service (SaaS), or
redistributing it under a different brand — without a separate license.

`KHost.Plugins.Sdk` is MIT so that plugins are unencumbered: you compile against
that assembly, ship a copy of it with your plugin, and license your own plugin
however you like — copyleft included. The non-compete term above applies to
KHost itself, not to anything you build on the SDK.

**Commercial, SaaS, and OEM licenses are available** for those uses — contact
Michael Riddle <riddlemd@gmail.com>.

Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md),
with full license texts under [`licenses/`](licenses). FFmpeg is **not** distributed
with KHost; it is obtained by the user (see that file).
