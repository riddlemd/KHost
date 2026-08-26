---
name: run-khost
description: Launch and drive the KHost app (windowed or headless) to see a change working — run it, click through the UI, play a song on a screen, and shut it down without destroying the local queue and library. Use whenever asked to run, start, screenshot, or verify KHost in the real app.
---

# Running KHost

Follow it in order; the traps section is not optional reading.

The prose names a primitive — "stop the host gracefully", "capture the window" — and the
**Toolbox** at the end gives that primitive per OS. macOS and Windows rows were verified by doing
them, except where a row says otherwise. Linux rows are written from the same primitives and are
**unverified**: say so if you lean on one, rather than reporting its output as fact.

Driving mechanics only. If the point is to **test** a change rather than just see the app, load the
`test-khost` skill as well — it carries the fixed sequence, the measurements that count as proof,
and the reporting shape.

## The one rule that protects the user's data

**Every graceful exit clears the singer queue and queued performances.** The venue's
`ClearQueueOnClose` defaults on (`Venue.cs`), and the clear runs on `ApplicationStopping` — a window
close, a `SIGTERM`, a `WM_CLOSE` and a Ctrl+C are all the same path. The queue, library and users
live in `src/KHost.UserInterface/bin/Debug/net10.0/cache/` (`singer-queue.json`, `khost.db`).

So bracket ANY run that will stop the app: copy the cache before, restore it after the host has
fully exited.

**Back up immediately before stopping, not at launch.** Whatever the operator queued during the run
exists only in that cache until the clear runs, so a launch-time backup silently restores a stale
queue. Restore the whole `cache/` directory, not just `singer-queue.json` — an enqueued song is a
row in `khost.db`, and `All queued performances deleted` takes it out on the same exit.

Prove the restore from the relaunch log: `Singer queue loaded (N users)` with the N you started
from. A restore you did not verify is a restore that did not happen.

Only a **hard** kill skips the clear (`kill -9`, `taskkill /F`, `Stop-Process`). That is a way to
preserve a queue in an emergency, not a way to shut down — it also skips the ffmpeg sweep. Deleting
`bin/` destroys everything.

## Prefer headless — it is the only OS-neutral way to drive the app

`--headless` serves the console as an ordinary page at `http://localhost:5251`, so it drives with
browser tooling and reads with the DOM instead of screenshot coordinate math. Every
platform-specific paragraph below exists only because the Photino window is not a browser tab.

Check one thing before betting on it: **the browser your tooling drives may not be on the same
machine as the app.** The symptom is unambiguous — the shell's `curl http://localhost:5251/` returns
200 while the browser reports `ERR_CONNECTION_REFUSED` on the same URL. When that happens headless
buys nothing and the windowed path is all there is. (Observed on this Windows host, confirmed twice
with the app up and serving.)

Only the window itself needs the windowed run: native chrome, `SetSize`, and the appliance lockdown.

## Launch

```bash
dotnet run --project src/KHost.UserInterface                # windowed (the real app)
dotnet run --project src/KHost.UserInterface -- --headless  # no window; console at localhost:5251
dotnet run --project src/KHost.AppHost                      # with the Aspire dashboard
```

Launch it as a background job of whatever harness you are in, with output going to a file — the
process runs until you stop it, and you want its log greppable while it runs.

- Listens on `http://localhost:5251` (bound `0.0.0.0`). A second launch refuses to start: the lock
  is a `FileShare.None` handle on `bin/Debug/net10.0/.instance.lock` (`Program.cs`), and the OS
  drops that handle even on a kill, so it never goes stale — a refusal means something really is
  running. Find it with the toolbox's *what holds port 5251*.
- Build separately with `dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"` so an open IDE's
  `bin/` is not locked, and run it **from the repo root**: `MSB1009: Project file does not exist`
  means you are in a subdirectory, not that the solution is missing. Watch for a shell whose working
  directory persists between commands — one earlier `cd` is enough to cause it.
- Don't rebuild into `bin/` while the app is running; it swaps the binaries underneath it.
- First build needs `npm install` in `src/KHost.UserInterface/` (fails on `copy:vendors`).
- SCSS compiles inside the build. To confirm a style change landed without launching anything, grep
  the compiled output: `grep -o 'kh-card__body{[^}]*}' src/KHost.UserInterface/wwwroot/css/app.css`.

**Readiness.** Poll HTTP rather than sleeping, in both modes — the window appears a beat before the
server answers:

```bash
curl -s -o /dev/null -w '%{http_code}\n' --retry 60 --retry-delay 2 --retry-connrefused \
  --max-time 10 http://localhost:5251/
```

Startup is confirmed by `Singer queue loaded (N users)`. File log:
`src/KHost.UserInterface/bin/Debug/net10.0/logs/YYYYMMDD.log`.

## Driving the windowed UI

The Photino window is not a browser tab: browser-extension tooling cannot reach it on any OS. What
replaces it differs per OS, and the three capabilities are independent — you can often capture
without being able to click.

| | macOS | Windows | Linux |
|---|---|---|---|
| Read window geometry | AppleScript `System Events` | `user32!GetWindowRect` | `xdotool getwindowgeometry` *(unverified)* |
| Capture the window | `screencapture -l <id>` | `user32!PrintWindow` flag 2 | `import -window <id>` *(unverified)* |
| Move / resize | AppleScript, two statements | `user32!MoveWindow` *(unverified)* | `wmctrl -r -e` *(unverified)* |
| Click / type | `cliclick` (Homebrew) | **see below** | `xdotool` *(unverified)* |

**Capture the window, not the desktop** — it keeps the rest of the screen out of the image and does
not require raising the app. Both verified methods capture without foregrounding, so a capture is
safe to take while the operator is using the app.

On Windows, `PrintWindow` with flag 2 (`PW_RENDERFULLCONTENT`) is the flag that matters: WebView2
composites out of process, and the default flag returns the black rectangle this technique is
notorious for. Sanity-check a capture by sampling distinct colours before trusting it — an all-black
image is a failed capture, not a broken theme.

### Synthetic input is the one thing you may not be able to do

There is no verified recipe for synthetic clicks or keystrokes on Windows here, for a reason worth
knowing before you spend an hour on it: **an agent harness may block input injection outright.** On
this host `SetCursorPos` + `mouse_event` was refused by the permission layer, and a self-hosted
WinForms probe window reported `Visible=True` with a valid handle while `FindWindow` could not see
it at all. Neither is a bug in the technique; both are the sandbox.

So: try the toolbox recipe once. If it is blocked or silently does nothing, **stop and say so** —
prefer headless, or make the change and ask the operator to look. Do not escalate into
window-station spelunking, and never fall back to injecting clicks blind: they land in whatever has
focus, which is how a stray Enter in the singer field creates a singer.

If you do drive natively, raise the window in the same command as the click — anything else on
screen can be in front of that region.

### Coordinates

Coordinates are **window-relative**, so they survive the window being moved or snapped:

| Target | Offset within a 1440×900 window |
|---|---|
| Screens dialog button (monitor icon, top right) | (1374, 53) |
| Media search box | (813, 320) |
| Media search button | (1366, 320) |

These are for a **1440×900** window. The layout reflows at other sizes and the operator may well
have resized it — this host's window was found at 974×1159 — so read the rect first and re-derive
from a capture whenever it does not match.

Two conversions, and they are not the same number:

- **To click:** `screen_point = window_origin + offset`. Read the origin, never assume it. A Windows
  window rect starts at its invisible resize border, so a snapped window reported `-7,0` here;
  assuming `0,0` puts every click 7px off. macOS reserves a menu bar at the top instead.
- **To read a capture:** `image_pixel = offset × (image_width ÷ window_width)`. That factor is 2 on
  a macOS Retina display and was **1** on this Windows host (a 974×1159 window captured 974×1159).
  Compute it from the image you actually got rather than carrying a 2 around.

The window's own close button sits at opposite ends of the title bar on macOS and Windows. Don't
click it — use the toolbox's graceful stop, which is OS-neutral in effect and greppable in the log.

Gotchas that hold everywhere: Return in the search box does NOT submit — click the search button.
Dialogs close via their X, which moves with dialog height, so re-capture and re-derive it.

## A full playback pass

1. Enqueue a song for the selected singer (search → Enqueue). The library ships with test media only
   if previously imported; check the `Media` count in `cache/khost.db`.
2. Screens dialog → Launch (Local). The screen registers in ~1s: grep the log for
   `RegisterScreen sent`. It takes the audio + primary roles when alone.
3. Play from the singer's queue row. Evidence of health: `Command received: SetTimelineCommand`
   about once a second, and the Screens dialog row showing "Artist - Title ▶ Playing mm:ss / mm:ss".
4. Stop: expect `Playback stopping (fade=00:00:05)` and `Command received: StopCommand` in the same
   second, then `Queue rotated`.
5. Playback needs ffmpeg on PATH (one process per song, HLS). After shutdown there must be zero
   ffmpeg left — the sweep runs on `ApplicationStopping`.

## Shutdown and cleanup

Stop the host gracefully (toolbox) and confirm it in the log — `Singer queue cleared on close` and
`All queued performances deleted` are what prove the shutdown path ran rather than the process
merely dying. Both exit in ~2s and release 5251; lingering past ~5s is a bug, not slowness
(commit b3859c3).

**A launched screen outlives the host by design** (it shows "Lost the host" and waits). Kill any
`KHost.Screen2` during cleanup.

Then restore the cache backup with the host stopped, and prove the restore from the relaunch log.

## Screen2 by hand

Prefer the Screens dialog's **Launch** button — it provisions the key and passes the arguments.
Launching by hand needs a key first: `--key-file` is required, and Screen2 dies on startup with
"No --key-file was given" without one.

The key is 32 random bytes, base64, at `cache/screens/<sha256-hex-of-screen-id>.key` — the id is
hashed, so the filename never matches the screen's name.

```bash
dotnet run --project src/KHost.Screen2 -- \
  --server-uri http://localhost:5251/ipc/screen \
  --screen-id hand-test \
  --key-file "<cache>/screens/<hash>.key" \
  --log-level debug
```

Success is `RegisterScreen sent for <id>` then `IPC state: Connecting -> Connected`.

Two ways to write a key file that is subtly wrong, and neither reports an error — the host simply
looks for a key that is not there, or presents one that does not match:

- **A trailing newline in the hashed id** hashes to a different filename. Use `printf '%s'`, never
  `echo`. (`hand-test` hashes to `798e0bce9dcd…5014`; both toolbox recipes were cross-checked
  against that value.)
- **A carriage return in the key.** On Windows `openssl rand -base64 32 | tr -d '\n'` leaves a `\r`
  behind — 45 bytes where the key is 44. Strip both: `tr -d '\r\n'`. Written from PowerShell,
  `[IO.File]::WriteAllText(...)` adds neither a newline nor a BOM.

Its player page is embedded in the executable — no `screen-ui/` files on disk, page edits need a
rebuild. Logs land in `logs/<screen-id>-YYYYMMDD.log` beside its binary.

## Toolbox — one primitive per row

Windows note that costs a call every time it is forgotten: **Git Bash mangles `/`-prefixed switches
into paths**, so `tasklist /FI ...` and `taskkill /PID ...` fail with
`Invalid argument/option - 'C:/Program Files/Git/FI'`. Run those from PowerShell, or double the
slash (`//PID`).

**Find the host process** — it is `KHost.UserInterface`, not `dotnet`, once running.
- macOS/Linux: `pgrep -fl "bin/Debug/net10.0/KHost.UserInterface"`
- Windows: `Get-Process KHost.UserInterface`

**What holds port 5251**
- macOS: `lsof -nP -iTCP:5251 -sTCP:LISTEN`
- Windows: `netstat -ano | grep ':5251' | grep LISTENING` (last column is the PID)
- Linux: `ss -lptn 'sport = :5251'` *(unverified)*

**Stop the host gracefully** — runs the shutdown path, so the queue clear happens.
- macOS/Linux: `kill -TERM <pid>`
- Windows: `$p = Get-Process KHost.UserInterface; $p.CloseMainWindow(); $p.WaitForExit(15000)`
  (posts `WM_CLOSE`; `Stop-Process` and `taskkill /F` are hard kills and skip it)

**Kill a stray screen**
- macOS/Linux: `pkill -f KHost.Screen2`
- Windows: `Stop-Process -Name KHost.Screen2 -ErrorAction SilentlyContinue`

**Count leftover ffmpeg** — must print 0 cleanly, not error.
- macOS/Linux: `pgrep -c ffmpeg || echo 0`
- Windows: `(Get-Process ffmpeg -ErrorAction SilentlyContinue).Count`
- In any shell, `grep -c` **exits 1 when it counts zero**, so a `|| echo 0` fallback fires and
  prints a second `0`. Use the process API, or read only the first line.

**Primary screen working area** — for parking a window; measure it, never hardcode a half.
- macOS: `osascript -e 'tell application "Finder" to get bounds of window of desktop'`
- Windows: `Add-Type -AssemblyName System.Windows.Forms;
  [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea`
- The origin is not always `0,0`: macOS reserves the menu bar at the top (y starts at 34), while
  this Windows host reported `1920x1152 at 0,0`, the taskbar taking the bottom 48px.

**SHA-256 of a string, no trailing newline**
- macOS: `printf '%s' "$id" | shasum -a 256 | cut -d' ' -f1`
- Linux/Git Bash: `printf '%s' "$id" | sha256sum | cut -d' ' -f1`
- Windows: `[BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData(
  [Text.Encoding]::UTF8.GetBytes($id))).Replace('-','').ToLower()`

**32 random bytes, base64, no trailing whitespace**
- macOS/Linux: `openssl rand -base64 32 | tr -d '\r\n'`
- Windows: `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`
- 44 characters is correct; 45 means a stray `\r` came along.

**Copy / remove a directory tree**
- macOS/Linux/Git Bash: `cp -R src dst` / `rm -rf dst`
- Windows: `Copy-Item src dst -Recurse` / `Remove-Item dst -Recurse -Force`

**Scratch space for backups** — prefer the session's scratchpad directory over `/tmp`. `/tmp` does
exist in Git Bash on Windows, but it is not where the operator will look, and `$TEMP` there is a
Windows path (`C:\Users\<user>\AppData\Local\Temp`) that a POSIX-quoted command will mishandle.
