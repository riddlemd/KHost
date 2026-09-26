// Holds a song back before its zero, so a singer whose words start almost at once is led in.
//
// The host sends how long with the words, already worked out against its own setting. The hold runs
// the words' clock below zero rather than stopping it: the intro card and the count-in both read that
// clock, so they carry on across the hold and into the song without a join.

/// Beats counted down over a bar made here, the same as the count-ins timings carry.
const LEAD_IN_STEPS = 4;
const LEAD_IN_STEP_SECONDS = 1;

/// When the first page shows, or null for a timing with none.
function firstPageAt(lyrics) {
    const pages = (lyrics && lyrics.pages) || [];
    return pages.length > 0 ? Math.min(...pages.map((page) => page.showFromSeconds)) : null;
}

/// The timing with one bar running from `-seconds` to the first page: a count-in the song already
/// opens with is started earlier, and a song with none gets one. Either way there is one bar and one
/// countdown, never two. Returns the timing unchanged when there is nothing to lead in to.
function withLeadInCountIn(lyrics, seconds) {
    const pageAt = firstPageAt(lyrics);
    if (!(seconds > 0) || pageAt === null) return lyrics;

    const countIns = lyrics.countIns || [];
    const opening = countIns.filter((c) => c.startSeconds < pageAt)
        .sort((a, b) => a.startSeconds - b.startSeconds)[0];

    if (opening) {
        return {
            ...lyrics,
            countIns: countIns.map((c) => (c === opening ? { ...c, startSeconds: -seconds } : c)),
        };
    }

    // Where a timing puts its own bar, as a share of its page: across the middle, below centre.
    const bounds = lyrics.bounds || { x: 0, y: 0, width: 640, height: 360 };
    const first = lyrics.pages.find((page) => page.showFromSeconds === pageAt) || {};
    const runUp = seconds + pageAt;

    return {
        ...lyrics,
        countIns: [{
            startSeconds: -seconds,
            endSeconds: pageAt,
            position: {
                x: bounds.x + bounds.width * 0.09375,
                y: bounds.y + bounds.height * 0.59,
                width: bounds.width * 0.8125,
                height: bounds.height * 0.073,
            },
            stepSeconds: LEAD_IN_STEP_SECONDS,
            steps: Math.min(LEAD_IN_STEPS, Math.floor(runUp / LEAD_IN_STEP_SECONDS)),
            active: first.active || null,
            inactive: first.inactive || null,
            border: { r: 0, g: 0, b: 0 },
            borderWidth: bounds.height / 192,
        }, ...countIns],
    };
}

/// The hold itself. `onElapsed` runs once, when a hold that was started runs out on its own.
///
/// A timer rather than rAF, as the stop fade uses: rAF stops while the window is occluded, and a
/// hold that never runs out never starts the song.
function createLeadInHold(onElapsed, now = () => performance.now()) {
    let armedSeconds = 0;
    let remainingMs = 0;
    let startedAt = null;
    let active = false;
    let timer = null;

    function clearTimer() {
        if (timer !== null) clearTimeout(timer);
        timer = null;
    }

    function run() {
        startedAt = now();
        timer = setTimeout(() => {
            timer = null;
            if (!active || startedAt === null) return;

            active = false;
            startedAt = null;
            remainingMs = 0;
            onElapsed();
        }, remainingMs);
    }

    return {
        /// Whether a hold is under way, running or paused: the song has not started under it.
        get active() { return active; },
        get paused() { return active && startedAt === null; },
        get armed() { return armedSeconds > 0; },

        /// Seconds left to hold, which is how far below zero the words' clock sits.
        get remaining() {
            if (!active) return 0;
            const left = startedAt === null ? remainingMs : remainingMs - (now() - startedAt);
            return Math.max(0, left) / 1000;
        },

        /// Readies a hold for the next play from the start. Zero or less readies none.
        arm(seconds) { armedSeconds = seconds > 0 ? seconds : 0; },

        /// Starts the armed hold, answering whether one started. Once only: a resume starts none.
        start() {
            if (active || armedSeconds <= 0) return false;

            remainingMs = armedSeconds * 1000;
            armedSeconds = 0;
            active = true;
            run();
            return true;
        },

        pause() {
            if (!active || startedAt === null) return;

            remainingMs = Math.max(0, remainingMs - (now() - startedAt));
            startedAt = null;
            clearTimer();
        },

        resume() {
            if (!active || startedAt !== null) return;
            run();
        },

        /// Drops the hold and anything armed, without starting the song.
        cancel() {
            clearTimer();
            active = false;
            startedAt = null;
            remainingMs = 0;
            armedSeconds = 0;
        },
    };
}
