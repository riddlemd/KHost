// Notices the machine waking from sleep, which the page is otherwise never told about.
//
// Why clocks rather than an event: nothing WebKit raises is reliable for it here. The context's
// statechange never fired — it read 'running' after the wake that silenced it — visibilitychange
// is about the window, not the machine, and devicechange is withheld from a page with no capture
// permission, which this one never asks for.

/// How much further the wall clock may run than the page's own clock between two ticks.
/// performance.now() stands still while the machine sleeps and Date.now() does not.
const WAKE_SLEEP_GAP_MS = 10000;

/// A tick this late on both clocks counts too, in case an engine's performance.now() counts sleep.
/// Throttling cannot reach it while a song plays: WebKit does not throttle a page playing audio.
const WAKE_STALL_GAP_MS = 60000;

/// Returns a tick to call from a timer that already runs; `onWake(asleepMs)` fires once per wake.
function createWakeWatch(onWake, clocks = {}) {
    const wallNow = clocks.wallNow ?? (() => Date.now());
    const monoNow = clocks.monoNow ?? (() => performance.now());

    let lastWall = wallNow();
    let lastMono = monoNow();

    return function tick() {
        const wall = wallNow();
        const mono = monoNow();
        const wallGap = wall - lastWall;
        const monoGap = mono - lastMono;

        lastWall = wall;
        lastMono = mono;

        if (wallGap - monoGap > WAKE_SLEEP_GAP_MS || monoGap > WAKE_STALL_GAP_MS) onWake(wallGap);
    };
}
