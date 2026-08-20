---
name: run-khost
description: Launch and drive the KHost app (windowed or headless) to see a change working — run it, click through the UI, play a song on a screen, and shut it down without destroying the local queue and library. Use whenever asked to run, start, screenshot, or verify KHost in the real app.
---

# Running KHost

Everything here was verified by actually doing it on macOS. Follow it in order; the traps
section is not optional reading.

## The one rule that protects the user's data

**Every graceful exit clears the singer queue and queued performances** (the venue's
`ClearQueueOnClose` defaults on, and the clear runs on `ApplicationStopping` — window close,
SIGTERM, Ctrl+C alike). The queue, library, and users all live in
`src/KHost.UserInterface/bin/Debug/net10.0/cache/` (`singer-queue.json`, `khost.db`).

So bracket ANY run that will stop the app:

```bash
CACHE=src/KHost.UserInterface/bin/Debug/net10.0/cache
cp -R "$CACHE" /tmp/khost-cache-backup      # before
# ... run, test, stop ...
rm -rf "$CACHE" && cp -R /tmp/khost-cache-backup "$CACHE"   # after, host stopped
```

Only a hard kill (`kill -9`) skips the clear. Deleting `bin/` destroys everything.

## Launch

```bash
# Windowed (the real app: native Photino window, 1440x900 at screen (0,34))
nohup dotnet run --project src/KHost.UserInterface > /tmp/khost.log 2>&1 &

# Headless (development: no window, console served to a browser)
nohup dotnet run --project src/KHost.UserInterface -- --headless > /tmp/khost.log 2>&1 &

# With the Aspire dashboard
dotnet run --project src/KHost.AppHost
```

- Listens on `http://localhost:5251`. A second launch refuses to start (exclusive
  `.instance.lock`) — check `lsof -nP -iTCP:5251 -sTCP:LISTEN` first and stop what holds it.
- Build separately with `dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"` so an open
  IDE's `bin/` isn't locked. Don't rebuild while the app is running — it swaps the binaries.
- First build needs `npm install` in `src/KHost.UserInterface/` (fails on `copy:vendors`).

**Readiness:**

```bash
# Headless: poll HTTP
until [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5251/)" = "200" ]; do sleep 1; done

# Windowed: poll for the visible process (~5s)
osascript -e 'tell application "System Events" to get name of every process whose visible is true' | grep -q KHost.UserInterface
```

Startup is confirmed by `Singer queue loaded (N users)` in the log (file log:
`bin/Debug/net10.0/logs/YYYYMMDD.log`).

## Driving the windowed UI

The Photino window is not a browser tab: the Claude Chrome extension cannot reach it, and
AppleScript `click at` fails with accessibility error `-25208`. **`cliclick` works**
(`/opt/homebrew/bin/cliclick`, Homebrew). Without cliclick, fall back to launch + screenshot
verification only; `osascript ... set frontmost` still works for raising windows.

The loop: screenshot a region → read it → map coordinates → click → screenshot again.

```bash
screencapture -R0,34,1440,900 -o -x /tmp/shot.png    # host window region
```

Coordinate math: screen_point = image_pixel × (region_width ÷ image_width) + region_offset.
On a 2x display a 1440-wide region renders 2880 pixels.

Stable coordinates (host window at 0,34, size 1440×900):

| Target | Screen coords |
|---|---|
| Host window red close button | (14, 49) |
| Screens dialog button (monitor icon, top right) | (1374, 87) |
| Media search box / search button | (813, 354) / (1366, 354) |
| Screen2 window | region `-R80,80,1280,720`, its close button (94, 95) |

Gotchas: Return in the search box does NOT submit — click the search button. Dialogs close
via their X (screenshot to locate it; it moves with dialog height).

## A full playback pass

1. Enqueue a song for the selected singer (search → Enqueue). The library ships with test
   media only if previously imported; check `Media` count via sqlite3 on `cache/khost.db`.
2. Screens dialog → Launch (Local). The screen registers in ~1s: grep the log for
   `RegisterScreen sent`. It takes the audio + primary roles when alone.
3. Play from the singer's queue row. Evidence of health: `Command received:
   SetTimelineCommand` about once a second, and the Screens dialog row showing
   "Artist - Title ▶ Playing mm:ss / mm:ss".
4. Stop: expect `Playback stopping (fade=00:00:05)` and `Command received: StopCommand` in
   the same second, then `Queue rotated`.
5. Playback needs ffmpeg on PATH (one process per song, HLS). After shutdown there must be
   zero `pgrep ffmpeg` leftovers — the sweep runs on ApplicationStopping.

## Shutdown and cleanup

- Close the window (cliclick the red X) or SIGTERM the `bin/Debug/net10.0/KHost.UserInterface`
  process — both exit in ~2s, log `Singer queue cleared on close`, and release 5251.
  If the process lingers past ~5s, that's a bug, not slowness (see commit b3859c3).
- **A launched screen outlives the host by design** (shows "Lost the host" and waits).
  `pkill -f KHost.Screen2` during cleanup.
- Restore the cache backup, then relaunch headless if one was running before.

## Screen2 by hand

```bash
dotnet run --project src/KHost.Screen2 -- --server-uri http://localhost:5251/ipc/screen --screen-id test --log-level debug
```

Its player page is embedded in the executable — no `screen-ui/` files on disk, page edits
need a rebuild. Logs land in `logs/<screen-id>-YYYYMMDD.log` beside its binary.
