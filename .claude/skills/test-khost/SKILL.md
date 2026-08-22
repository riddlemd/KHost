---
name: test-khost
description: Walk KHost through a consistent test pass after a change — the cheap gates to clear first, the fixed core sequence, the measurements that count as proof (clock rate, single-fire counts, cadence, log volume), extra passes per change area, and the traps that have already cost time. Use whenever asked to test, verify, walk through, regression-check, smoke-test, or confirm a change works in the real app.
---

# Testing KHost

The `run-khost` skill is *how* to drive the app — launch, cliclick, coordinates, shutdown. Invoke
it for the mechanics. This skill is *what to check and what counts as proof*, so two walkthroughs a
month apart are comparable.

The rule underneath all of it: **a walkthrough reports measurements, not impressions.** "Playback
looked fine" is worth nothing; "0:35 → 0:43 across an 8s gap, so exactly one clock at 1× real time"
is the finding.

## 1. Earn the launch first

Driving the UI is the slowest way to learn anything. Spend it only after the cheap gates pass:

```bash
dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"
dotnet test tests/KHost.UnitTests
```

Then the mutation sweep on whatever tests the change added (AGENTS.md and the global rules cover
the procedure). A walkthrough that finds a bug a unit test should have caught means the sweep was
skipped — go back and do it rather than debugging through screenshots.

Launch the app to answer a question the tests structurally cannot: does the wiring hold in the real
Blazor circuit, does the screen actually receive the command, does the clock run at the right rate
against a real wall clock.

## 2. Protect the data before anything that stops the app

Every graceful exit clears the singer queue. `run-khost` has the bracket; use a run-specific backup
name so a second session cannot clobber the first:

```bash
CACHE=src/KHost.UserInterface/bin/Debug/net10.0/cache
BACKUP=/tmp/khost-cache-backup-$(date +%H%M%S)
cp -R "$CACHE" "$BACKUP"
```

Restore only after the host has fully exited. Then prove the restore: the singer queue JSON holds
the users it held before, any test singer you created is gone from `Users`, and the `Media` count is
unchanged. A restore you did not verify is a restore that did not happen.

## 3. Baseline before, same numbers after

```bash
L=src/KHost.UserInterface/bin/Debug/net10.0/logs/$(date +%Y%m%d).log
wc -l < "$L"                  # log lines so far today
pgrep -fl ffmpeg | wc -l      # should be 0
```

## 4. The core pass — run this every time

Each step has a check that fails loudly rather than a screenshot you squint at.

| # | Action | What proves it worked |
|---|---|---|
| 1 | Launch windowed, park left half | Window in <10s; read the size back — both AppleScript calls can report success and move nothing |
| 2 | Add a singer | Row appears, singer auto-selects, right-hand panels appear |
| 3 | Search a song | Results table populated |
| 4 | Enqueue it | **All three panels** update: singer row count, queue panel row, search row state |
| 5 | Launch a local screen | `Audio and primary are both on <id>` logged **once** |
| 6 | Play | Now Playing shows title + singer, transport flips to ⏸ |
| 7 | Sample the playhead twice, ≥8s apart | Delta equals wall-clock delta — see below |
| 8 | Pause, resume, seek | Playhead freezes, resumes, jumps; log lines match the UI |
| 9 | Seek to ~98% and let it end naturally | Exactly one each of concluded / dequeued / rotated |
| 10 | Close the window | Exits in <5s, closes its screen, no ffmpeg left |

### Clock rate — the one measurement worth doing properly

Two captures of the Now Playing region at least 8 seconds apart; compare the displayed time to the
wall clock between them. Equal means one clock at 1× real time. Roughly double means an orphaned
timer — the failure mode that degrades a long session into a freeze. A gap shorter than 8s cannot
tell 1× from 2× reliably.

### Single-fire counts

Step 9 catches double-rotation, and it needs counting rather than reading:

```bash
L=src/KHost.UserInterface/bin/Debug/net10.0/logs/$(date +%Y%m%d).log
grep -c 'Playback concluded'   "$L"
grep -c 'Dequeued performance' "$L"
grep "Queue rotated" "$L" | tail -3        # timestamps, not just the count
```

Count against the *conclusion's timestamp*, not the file total — the file carries earlier runs and
one rotation per singer added, so a raw count of 5 can still be correct.

## 5. Add a pass for what changed

| Touched | Also walk |
|---|---|
| Playback / timer / position | Clock rate at 1×; pause→resume→seek→resume; natural end; that the queue panels do **not** re-query on the position tick |
| Screens / IPC / roles | Two screens up, kill one, confirm the dialog drops it live; role log lines fire once per real move |
| Queue / rotation | Several singers, several songs each; rotation order after a natural end |
| Search / library | A term that hits, one that misses, one with an apostrophe or accent |
| Any panel or SCSS | Drag the panel splitter narrow — panels answer to `@container`, so a 180px panel at 1440 is a real layout |
| Cast | Needs the emulator on 127.0.0.1:8009; otherwise say it went untested rather than implying it passed |

## 6. Healthy baselines (measured 2026-08-22, one song end to end)

Numbers to compare against, not thresholds to enforce:

- Host CPU **~3%** while playing, **~0%** idle
- `SetTimelineCommand` on the screen at **1.00/s**, steady (`14:27:05.865, :06.867, :07.868…`)
- Host log **~140 lines for a whole day** of normal use
- Window close to process exit: **1s**
- Zero `ffmpeg` after shutdown

For contrast, the 2026-08-17 timeline feedback loop produced **391,502 log lines in two minutes**.
A log growing by thousands of lines a minute is the loudest signal this app produces — check the
line count before assuming a slow UI is a rendering problem.

## 7. Traps that have already cost time

- **A dialog's X moves with its height.** The Screens dialog grows as screens connect. Re-screenshot
  and re-derive the X every time; reusing the coordinate from before a screen connected misses.
- **Confirm a dialog closed before the next click.** A missed close leaves the next click landing on
  whatever is underneath — that is how a stray click launched a second screen mid-run.
- **Screen2 windows open on top of the console.** Park them on the right half before continuing, or
  every screenshot after that is a black rectangle.
- **Region math:** `screen_x = image_x / 2`, `screen_y = region_offset_y + image_y / 2` on a 2× display.
  Forgetting the offset puts every click ~34px high.
- **`HALF` is not 720.** Measure it (`run-khost` has the incantation) — this machine is 1470 wide, so
  half is 735, and a wrong half makes the layout look broken when it is not.
- **Don't rebuild while the app is running** — it swaps the binaries under the live process. If you
  built during the session, restart before trusting anything you see.
- **`rtk grep` mangles large-output greps.** For log analysis over big files use `python3` or plain
  `awk`; a truncated grep looks like an absent signal.

## 8. Report it the same way every time

1. A table of what was exercised and the result
2. The measurements, with numbers — clock delta, cadence, counts, CPU, log lines
3. Anything you did **not** test, said plainly
4. Anything odd you saw but did not chase, flagged as unrelated if it is

That last one matters: the EF `Skip/Take` without `OrderBy` warning on every search was found this
way. Note it; don't silently fold it into the change under test.

## 9. Stop and ask

Two or three failed attempts at the same click, an unexpected dialog, the window not responding, or
anything that wants a fifth approach — stop, say what you tried and what happened, and ask. Do not
keep clicking. A walkthrough that ends in "here is where it stalled" is a useful result; one that
ends in twenty blind clicks is not.
