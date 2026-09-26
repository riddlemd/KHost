# KHost

Karaoke hosting software built on **.NET 10**. KHost pairs a Blazor Server host console, where you
run the night, with a local screen app that shows the song to the room. It runs on Windows, Linux
and macOS.

## A night with KHost

- **Singer queue and rotation.** Singers join the queue and the rotation decides who is up next;
  the screen's marquee names the singers coming up.
- **Search and enqueue.** Search the library, or a plugin's media provider, and put a song on a
  singer's list. Songs a provider still has to download show their progress on the Downloads page.
- **The screen.** One display shows the song: the local screen app, or a device a plugin supplies.
  A venue can put a QR code on it.
- **Break music.** Between singers KHost fills the room from break music, and a venue can name the
  track that is playing in a corner of the screen.
- **Ads.** A venue can choose an ad playlist; ads play in the gap after a performance, never over one.
- **Tips.** Tips are recorded against the singer and the venue they were taken at.

The library, singers and groups live in SQLite; the queue and venue state live in a JSON cache
under `./cache/`.

## How a song reaches the room

When a song starts, a **renderer** answers what the connected display gets. It answers with one of
two things, or both:

- **Stems** that the display mixes for itself. A display that mixes runs no ffmpeg at all.
- **A stream** that the host encodes with ffmpeg into HLS at play time, one ffmpeg per song. This
  is the fallback, and it takes any file nothing else claims.

There is **no pre-render**. Nothing is encoded ahead of time and nothing is cached between songs.

There is **one display at a time**: the local screen, or a device a plugin supplies (for example a
Cast receiver). Picking one disconnects the other. The display that is up reports the song's
position, and the host trusts only that report.

During a song the host can shift the **key** (±6 semitones), the **tempo** (±50%) and, where the
song carries stems, the **vocal level**. A display that can ride a level itself does so; otherwise
the host rebuilds the stream at the playhead with the new mix. The local screen covers that rebuild
by keeping the old stream playing until the new one has sound.

Design notes: [docs/media-renderer.md](docs/media-renderer.md) and
[docs/display-provider.md](docs/display-provider.md).

## Plugins

A plugin runs in-process and can add:

- **media providers**: search and download songs into the library;
- **break music** providers;
- **queue rotation** modes;
- **display providers**: somewhere else the song comes out, such as a network receiver;
- **formats**: a plugin that ships its own container can describe it (`IMediaProbe`), render it
  (`IMediaRenderer`) and hand over its timed lyrics (`ITimedLyricsProvider`), and can gate what may
  be queued or played from it (`IMediaPlaybackGate`);
- **QR codes** it offers the screens, which the venue chooses whether to draw;
- **buttons** on its row on the Plugins page, and tables and prompts reached from them.

Hosts install plugins from the **Available** tab on the Plugins page, which reads the published
[`plugin-catalog.json`](plugin-catalog.json). The catalog is the trust root: a release is offered
only over https with a `sha256`, and the download is hashed and checked before anything is written.
An install is staged and applied on the next start, never into a running host.

A plugin builds against two NuGet packages, **`KHost.Abstractions`** (the interfaces and models)
and **`KHost.Common`** (helpers over them), both MIT. It references them with
`ExcludeAssets="runtime"` and ships no copy, because the host already has both. Until the packages
are released, `./build/pack-contracts.sh` packs them into a local feed:

```bash
dotnet nuget add source ~/.nuget/khost-local -n khost-local   # once per machine
./build/pack-contracts.sh
```

Catalog entries are written by a tool, never by hand:

```bash
dotnet run --project tools/KHost.CatalogSync -- <owner/repo>
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/): the build needs `node_modules` (`npm install` in
  `src/KHost.UserInterface`)
- [FFmpeg](https://ffmpeg.org/download.html), with `ffmpeg` and `ffprobe` on `PATH`. FFmpeg is not
  distributed with KHost.

### Hardware

KHost encodes video on the CPU, with no hardware acceleration, so the processor decides whether it
keeps up.

| | Minimum | Recommended |
|---|---|---|
| CPU | 8th-gen Core i3 / Ryzen 3 2000 / N100 | Any modern quad-core, or Apple silicon |
| RAM | 8 GB | 16 GB |
| Storage | 128 GB SSD | 256 GB SSD, plus room for the library |
| Audio | Stereo line out, or a class-compliant USB interface | The same |
| Network | Same subnet as any network display | Wired |

A song that is streamed is encoded while it plays, so the encode has to run faster than real time.
To check a machine, run ffmpeg against a real karaoke file and read the `speed=` figure at the end:

```bash
ffmpeg -benchmark -i song.mp4 -c:v libx264 -preset veryfast -profile:v main -c:a aac -f null -
```

## Running from source

```bash
git clone https://github.com/riddlemd/KHost.git
cd KHost
(cd src/KHost.UserInterface && npm install)
dotnet run --project src/KHost.UserInterface                 # native window
dotnet run --project src/KHost.UserInterface -- --headless   # console at http://localhost:5251
```

The first run walks through setup at `/setup`. Only one instance runs at a time: port 5251 is held
by a lock, so stop one before starting another.

## Building and testing

```bash
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"
dotnet test tests/KHost.UnitTests
dotnet test tests/KHost.IntegrationTests   # drives real ffmpeg
```

SCSS compiles inside `dotnet build`; there is no separate sass step. The unit tests are hermetic
and never skip. The integration tests need ffmpeg and fail without it; set
`KHOST_SKIP_ENVIRONMENT_TESTS=1` to accept skipping them.

## Further reading

- [AGENTS.md](AGENTS.md): architecture, rules and conventions, for contributors and coding agents.
- [docs/](docs): design notes on renderers, display providers and generated backgrounds.
- The [KHost wiki](https://github.com/riddlemd/KHost/wiki): setup, configuration and plugin guides.
- [CONTRIBUTING.md](CONTRIBUTING.md): the terms contributions are accepted under.

## License

KHost is licensed under the [PolyForm Shield License 1.0.0](LICENSE), **except for the two
projects a plugin builds against**: [KHost.Abstractions](src/KHost.Abstractions/LICENSE) and
[KHost.Common](src/KHost.Common/LICENSE), which are MIT.

You may use, modify, and self-host KHost for any purpose, **including commercial use** (for
example, running it to host your own karaoke events). You may **not** use it to provide a product
or service that competes with KHost, including offering KHost or a derivative as a hosted/managed
service (SaaS), or redistributing it under a different brand, without a separate license.

Those two are MIT so that plugins are unencumbered: you compile against them and license your own
plugin however you like, copyleft included. The non-compete term above applies to KHost itself,
not to anything you build on them.

**Commercial, SaaS, and OEM licenses are available** for those uses. Contact
Michael Riddle <riddlemd@gmail.com>.

Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), with full
license texts under [`licenses/`](licenses). FFmpeg is **not** distributed with KHost; it is
obtained by the user (see that file).
