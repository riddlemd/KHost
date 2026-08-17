// Plays the host's HLS stream. There is no MediaSource pump, no buffer eviction and no quota
// handling here any more: the host transcodes and this page just points <video> at a URL.
// WKWebView plays HLS from a bare src; a Chromium-based webview (WebView2 on Windows) does not
// and will need hls.js bundling before this screen works there.

const video = document.getElementById('video');
const placeholder = document.getElementById('placeholder');

function send(payload) {
    if (window.external && window.external.sendMessage) {
        window.external.sendMessage(JSON.stringify(payload));
    }
}

function reportError(message) {
    send({ type: 'error', message: String(message) });
}

let currentVolume = 1;

function load(url, autoplay) {
    video.style.transition = 'opacity 120ms linear';
    video.style.opacity = '1';
    video.volume = currentVolume;

    if (video.canPlayType('application/vnd.apple.mpegurl') === '') {
        reportError('no native HLS support in this webview');
        return;
    }

    video.src = url;
    if (autoplay) video.play().catch((e) => reportError(`play: ${e}`));
}

function teardown() {
    try { video.pause(); } catch { /* ignore */ }
    try { video.removeAttribute('src'); video.load(); } catch { /* ignore */ }
}

async function fadeOutAndStop(fadeMs) {
    const startVolume = video.volume;
    const startedAt = performance.now();

    video.style.transition = `opacity ${fadeMs}ms linear`;
    video.style.opacity = '0';

    await new Promise((resolve) => {
        const tick = () => {
            const progress = Math.min(1, (performance.now() - startedAt) / fadeMs);
            video.volume = startVolume * (1 - progress);
            if (progress < 1) requestAnimationFrame(tick); else resolve();
        };
        tick();
    });

    teardown();
    video.style.transition = 'opacity 120ms linear';
    video.volume = currentVolume;
    placeholder.hidden = false;
    send({ type: 'state', position: 0, duration: 0, playing: false });
}

// --- Sync group -------------------------------------------------------------------------------
// Screens receive the same stream but attach at different moments, so left alone they play the
// same song several seconds apart forever. The host publishes where the song should be against
// its own clock; each screen steers itself onto that instead of onto its own start time.

// Correction is by seeking, never by trimming playbackRate. Two things force that: a trim is a
// pitch error, which a music app cannot carry even on a muted screen if it might ever be unmuted;
// and measurably, WKWebView's HLS pipeline walks currentTime *backwards* when the rate is off 1.0,
// so trimming destabilises the very thing it is meant to settle. Left at 1.0 two screens hold a
// constant offset to about 1ms/s, so one accurate alignment lasts minutes.
const REALIGN_THRESHOLD = 0.15;

// A seek costs a rebuffer, so the drift has to be genuine rather than one noisy sample.
const REALIGN_CONFIRMATIONS = 3;

let clockOffsetMs = 0;
let timeline = null;
let isTimingReference = false;

let driftConfirmations = 0;

function hostNowMs() {
    return Date.now() + clockOffsetMs;
}

/// Where the stream should be right now, or null when the group is not playing.
function expectedStreamTime() {
    if (!timeline) return null;
    if (!timeline.playing) return timeline.position;

    const elapsed = (hostNowMs() - timeline.anchorEpochMs) / 1000;
    // Before the anchor the group has not started yet; hold at the start position.
    return timeline.position + Math.max(0, elapsed);
}

function correct() {
    // The timing reference defines the timeline rather than chasing one, so it is never
    // corrected — there is nothing for it to be corrected towards.
    if (isTimingReference) {
        video.playbackRate = 1;
        return;
    }

    const expected = expectedStreamTime();
    if (expected === null || video.readyState < 2 || video.seeking) return;

    if (!timeline.playing) {
        video.playbackRate = 1;
        return;
    }

    // Every screen plays at true speed. The only correction is where the playhead sits.
    video.playbackRate = 1;

    const error = video.currentTime - expected;

    if (Math.abs(error) < REALIGN_THRESHOLD) {
        driftConfirmations = 0;
        return;
    }

    if (++driftConfirmations < REALIGN_CONFIRMATIONS) return;

    driftConfirmations = 0;
    try { video.currentTime = expected; } catch { /* outside the buffered range yet */ }
}

// setInterval, not requestAnimationFrame: rAF stops being serviced while the window is occluded,
// which silently freezes the correction exactly when a screen is most likely to have drifted.
setInterval(correct, 200);

function handleCommand(raw) {
    let message;
    try { message = JSON.parse(raw); } catch { return; }

    switch (message.type) {
        case 'load':
            placeholder.hidden = false;
            // The old timeline described the previous stream; keeping it would seek the new one
            // to a position that means nothing in it.
            timeline = null;
            driftConfirmations = 0;
            load(message.url, message.autoplay === true);
            break;
        case 'clock':
            clockOffsetMs = message.offsetMs || 0;
            break;
        case 'timeline': {
            const next = {
                position: message.position || 0,
                anchorEpochMs: message.anchorEpochMs || 0,
                playing: message.playing === true,
            };

            isTimingReference = message.timingReference === true;

            timeline = next;
            break;
        }
        case 'play':
            placeholder.hidden = true;
            video.play().catch((e) => reportError(`play: ${e}`));
            break;
        case 'pause':
            video.pause();
            break;
        case 'stop':
            timeline = null;
            fadeOutAndStop(Math.max(1, message.fadeMs || 0));
            break;
        case 'seek':
            // Seeking within a stream the page already holds, rather than restarting a transcode.
            try { video.currentTime = message.position || 0; } catch (e) { reportError(`seek: ${e}`); }
            break;
        case 'volume':
            currentVolume = Math.max(0, Math.min(1, message.value));
            video.volume = currentVolume;
            break;
        default:
            break;
    }
}

// The window has no controls of its own, so the page is the only place these gestures can live.
document.addEventListener('dblclick', () => send({ type: 'toggle-fullscreen' }));
window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') send({ type: 'exit-fullscreen' });
});

video.addEventListener('loadeddata', () => { placeholder.hidden = true; });
video.addEventListener('ended', () => send({ type: 'ended' }));
video.addEventListener('error', () => {
    const error = video.error;
    if (error) reportError(`media error ${error.code}`);
});

// The host polls nothing; position reaches it only through these reports.
setInterval(() => {
    const expected = expectedStreamTime();

    send({
        type: 'state',
        position: Number.isFinite(video.currentTime) ? video.currentTime : 0,
        duration: Number.isFinite(video.duration) ? video.duration : 0,
        playing: !video.paused && !video.ended && video.readyState > 2,
        // Stamped at sample time, not send time: the host builds the group timeline from the
        // leader's reports, and any latency it had to guess at would bias that timeline forever.
        sampledAtEpochMs: Date.now(),
        // Sync telemetry: without it a screen drifting off the group is invisible to the host.
        expected: expected === null ? -1 : expected,
        rate: video.playbackRate,
        readyState: video.readyState,
    });
}, 250);

if (window.external && window.external.receiveMessage) {
    window.external.receiveMessage(handleCommand);
}
