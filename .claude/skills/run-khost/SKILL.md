---
name: run-khost
description: Launch and drive the KHost app (windowed or headless) to see a change working — run it, click through the UI, play a song on a screen, and shut it down without destroying the local queue and library. Use whenever asked to run, start, screenshot, or verify KHost in the real app.
---

# Running KHost

Everything here was verified by actually doing it on macOS. Follow it in order; the traps
section is not optional reading.

Driving mechanics only. If the point is to **test** a change rather than just see the app, load the
`test-khost` skill as well — it carries the fixed sequence, the measurements that count as proof,
and the reporting shape.

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
#
# Park it on the LEFT half of the screen straight after launch. At its default size it covers
# the terminal, and the operator cannot read replies while the app is up. Measure the half
# rather than assuming it - a window even slightly narrower than half wastes screen the
# operator can see is empty:
#
#   HALF=$(osascript -e 'tell application "Finder" to get bounds of window of desktop' \
#     | awk -F', ' '{print int($3/2)}')
#   osascript -e "tell application \"System Events\" to tell process \"KHost.UserInterface\" \
#     to set position of window 1 to {0, 34}"
#   osascript -e "tell application \"System Events\" to tell process \"KHost.UserInterface\" \
#     to set size of window 1 to {$HALF, 864}"
#
# Position and size must be set in separate statements - setting both at once fails -10003.
# Read the size back afterwards - both statements can report success and leave the window as it
# was, and a screenshot of the wrong region looks like a layout bug:
#   osascript -e 'tell application "System Events" to tell process "KHost.UserInterface" to get size of window 1'
#
# Screenshot that region with: screencapture -R0,34,$HALF,864 -o -x out.png
# Recompute click coordinates against the resized window - the layout reflows.
nohup dotnet run --project src/KHost.UserInterface > /tmp/khost.log 2>&1 &

# Headless (development: no window, console served to a browser)
nohup dotnet run --project src/KHost.UserInterface -- --headless > /tmp/khost.log 2>&1 &

# With the Aspire dashboard
dotnet run --project src/KHost.AppHost
```

- Listens on `http://localhost:5251`. A second launch refuses to start (exclusive
  `.instance.lock`) — check `lsof -nP -iTCP:5251 -sTCP:LISTEN` first and stop what holds it.
  Stop with `pkill -f "bin/Debug/net10.0/KHost.UserInterface"`, not a single PID from `pgrep | head -1`:
  a leftover instance leaves the relaunch showing a blank 1x1 window that logs `SetSize(1, 1)`.
- Build separately with `dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"` so an open
  IDE's `bin/` isn't locked. Don't rebuild while the app is running — it swaps the binaries.
- First build needs `npm install` in `src/KHost.UserInterface/` (fails on `copy:vendors`).

**Readiness:**

```bash
# Headless: poll HTTP
until [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5251/)" = "200" ]; do sleep 1; done

# Windowed: poll for the window, not the process. The process turns visible a beat before its
# window exists, and every `window 1` call until then fails with -1719.
osascript -e 'tell application "System Events" to tell process "KHost.UserInterface" to get name of window 1' >/dev/null 2>&1
```

Startup is confirmed by `Singer queue loaded (N users)` in the log (file log:
`bin/Debug/net10.0/logs/YYYYMMDD.log`).

## Driving the windowed UI

The Photino window is not a browser tab: the Claude Chrome extension cannot reach it, and
AppleScript `click at` fails with accessibility error `-25208`. **`cliclick` works**
(`/opt/homebrew/bin/cliclick`, Homebrew). Without cliclick, fall back to launch + screenshot
verification only; `osascript ... set frontmost` still works for raising windows.

The loop: screenshot a region → read it → map coordinates → click → screenshot again.

Raise the window in the same command as the click. Anything else on screen — a browser — can be
in front of that region, and a blind click lands in it instead. Two mistakes to avoid: the region
offset belongs in the y (`screen_y = 34 + image_y / 2`), and typing after a shortcut that did not
land goes wherever focus actually is — a stray Enter in the singer field creates a singer.

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

Prefer the Screens dialog's **Launch** button — it provisions the key and passes the arguments.
Launching by hand needs a key first: `--key-file` is required, and the host normally writes one
per screen it launches. Without it Screen2 dies on startup with "No --key-file was given".

The key is 32 random bytes, base64, at `cache/screens/<sha256-hex-of-screen-id>.key` — the id is
hashed, so the filename never matches the screen's name. Provision one and launch:

```bash
CACHE="$PWD/src/KHost.UserInterface/bin/Debug/net10.0/cache"
SCREEN_ID=hand-test
HASH=$(printf '%s' "$SCREEN_ID" | shasum -a 256 | cut -d' ' -f1)

mkdir -p "$CACHE/screens"
openssl rand -base64 32 | tr -d '\n' > "$CACHE/screens/$HASH.key"
chmod 600 "$CACHE/screens/$HASH.key"

dotnet run --project src/KHost.Screen2 -- \
  --server-uri http://localhost:5251/ipc/screen \
  --screen-id "$SCREEN_ID" \
  --key-file "$CACHE/screens/$HASH.key" \
  --log-level debug
```

`printf '%s'` not `echo` — a trailing newline hashes to a different filename, and the host then
looks for a key that is not there. Success is `RegisterScreen sent for <id>` then
`IPC state: Connecting -> Connected` in the screen's output.

Its player page is embedded in the executable — no `screen-ui/` files on disk, page edits
need a rebuild. Logs land in `logs/<screen-id>-YYYYMMDD.log` beside its binary.
