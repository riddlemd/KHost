// Plays the host's HLS stream through hls.js, which demuxes the MPEG-TS segments in JS and feeds
// them to MSE. There is no native-HLS path: WKWebView would play the playlist from a bare src,
// but a web view that cannot run hls.js cannot serve as a screen anyway, and carrying a second
// path meant the two platforms failed differently and only one of them got tested.

// Two players. `video` is the one the room is hearing; `incoming` is one being brought up to
// speed behind it, so a rebuilt stream can take over without the room hearing the join.
const videos = [document.getElementById('video'), document.getElementById('video-b')];
let video = videos[0];
let incoming = null;

/// Commands shape the player that is about to be heard, which during a handover is the new one.
function target() { return incoming ?? video; }
const background = document.getElementById('background');
const still = document.getElementById('still');

const SCALING = { fit: 'contain', fill: 'cover', stretch: 'fill', original: 'none' };
const placeholder = document.getElementById('placeholder');
const blanked = document.getElementById('blanked');
const hostLost = document.getElementById('hostlost');
const marquee = document.getElementById('marquee');
const marqueeTrack = document.getElementById('marquee-track');
const marqueePin = document.getElementById('marquee-pin');

function send(payload) {
    if (window.external && window.external.sendMessage) {
        window.external.sendMessage(JSON.stringify(payload));
    }
}

function reportError(message) {
    send({ type: 'error', message: String(message) });
}

let currentVolume = 1;

let hls = null;
let incomingHls = null;

/// A handover that never becomes ready must not strand the change; take it anyway.
const HANDOVER_TIMEOUT_MS = 4000;
const CROSSFADE_MS = 120;

// A decode glitch can usually be recovered in place, but a source that never decodes would
// otherwise recover forever, so give up and let the host hear about it.
const MAX_MEDIA_RECOVERIES = 2;
let mediaRecoveries = 0;

/// Drops a handover that has not swapped yet, leaving whatever is playing alone.
function cancelHandover() {
    destroyHls(incomingHls);
    incomingHls = null;

    if (!incoming) return;

    retire(incoming);
    incoming = null;
}

function detachHls() {
    destroyHls(hls);
    hls = null;

    // A handover still in flight has to go with it, or its element keeps decoding into nothing.
    destroyHls(incomingHls);
    incomingHls = null;

    if (incoming) {
        retire(incoming);
        incoming = null;
    }
}

function onHlsError(_, data) {
    if (!data.fatal) return;

    if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
        hls.startLoad();
        return;
    }

    if (data.type === Hls.ErrorTypes.MEDIA_ERROR && mediaRecoveries < MAX_MEDIA_RECOVERIES) {
        mediaRecoveries++;
        hls.recoverMediaError();
        return;
    }

    reportError(`hls: ${data.details}`);
    detachHls();
}

function load(url, autoplay) {
    // Nothing to hand over from: a stopped or unstarted player takes the stream directly, which
    // is the path every fresh song uses and the one that has always worked.
    if (!hls || video.paused || video.readyState < 3) {
        video.style.transition = `opacity ${CROSSFADE_MS}ms linear`;
        video.style.opacity = '1';
        video.volume = currentVolume;

        detachHls();
        attach(video, url, autoplay, (h) => { hls = h; });
        return;
    }

    // Something is playing. Bring the replacement up behind it silently, and only swap once it
    // has sound to give — tearing the old one down first is the gap this exists to remove.
    const next = videos.find((v) => v !== video);

    retire(next);
    incoming = next;

    next.volume = 0;
    next.style.transition = 'none';
    next.style.opacity = '0';

    let swapped = false;
    const swap = () => {
        if (swapped) return;
        swapped = true;
        clearTimeout(timer);
        handOver(next);
    };

    const timer = setTimeout(swap, HANDOVER_TIMEOUT_MS);

    next.addEventListener('playing', swap, { once: true });

    attach(next, url, true, (h) => { incomingHls = h; });
}

/// Wires one element to a stream. The engine dance below is why this is shared rather than copied.
function attach(el, url, autoplay, keep) {
    mediaRecoveries = 0;

    if (!window.Hls || !Hls.isSupported()) {
        reportError('this webview cannot run hls.js: no Media Source Extensions');
        return;
    }

    const instance = new Hls({ preferManagedMediaSource: false });
    keep(instance);

    instance.on(Hls.Events.ERROR, onHlsError);
    // Autoplay waits for the manifest: the media element has nothing to play until then.
    instance.on(Hls.Events.MANIFEST_PARSED, () => {
        if (autoplay) el.play().catch((e) => reportError(`play: ${e}`));
    });
    instance.loadSource(url);

    // The two engines reject opposite things, so this tries the one that is fussier about origins
    // first and falls back rather than choosing by name.
    //
    // WebKit: hls.js left to itself reaches the element through URL.createObjectURL, and this page
    // is handed to the web view as a raw string — an opaque origin — so that URL comes out as
    // blob:null/… and WebKit refuses to load it (MEDIA_ERR_SRC_NOT_SUPPORTED, before hls.js sees
    // anything to report). srcObject carries no origin, and handing the source to attachMedia
    // makes hls.js adopt it instead of minting a URL.
    //
    // Chromium: srcObject takes only a MediaStream or a MediaSourceHandle, and throws TypeError on
    // a bare MediaSource — it has no MediaSource.handle to offer either. It has no quarrel with the
    // blob: URL, so hls.js attaches the ordinary way. Thrown, not silent: assigning to srcObject
    // outside a try would abandon this with the screen black and nothing reported to the host.
    const mediaSource = new MediaSource();

    try {
        el.srcObject = mediaSource;
    } catch {
        instance.attachMedia(el);
        return;
    }

    instance.attachMedia({ media: el, mediaSource });
}

/// Dissolves picture and sound from the outgoing player to the incoming one.
function handOver(next) {
    if (incoming !== next) return;

    const outgoing = video;
    const outgoingHls = hls;

    incoming = null;
    video = next;
    hls = incomingHls;
    incomingHls = null;

    next.style.transition = `opacity ${CROSSFADE_MS}ms linear`;
    next.style.opacity = '1';
    outgoing.style.transition = `opacity ${CROSSFADE_MS}ms linear`;
    outgoing.style.opacity = '0';

    const startedAt = Date.now();
    const fade = setInterval(() => {
        const progress = Math.min(1, (Date.now() - startedAt) / CROSSFADE_MS);

        try { next.volume = currentVolume * progress; } catch { /* detached mid-fade */ }
        try { outgoing.volume = currentVolume * (1 - progress); } catch { /* same */ }

        if (progress < 1) return;

        clearInterval(fade);
        destroyHls(outgoingHls);
        retire(outgoing);
    }, 16);
}

/// Stops an element and lets go of its source, without touching whatever is playing.
function retire(el) {
    try { el.pause(); } catch { /* ignore */ }
    try { el.srcObject = null; } catch { /* ignore */ }
    try { el.removeAttribute('src'); el.load(); } catch { /* ignore */ }
}

function destroyHls(instance) {
    if (!instance) return;

    try { instance.destroy(); } catch { /* ignore */ }
}

function teardown() {
    // Before the element is cleared: destroy() detaches the media it is driving.
    detachHls();
    try { video.pause(); } catch { /* ignore */ }
    // srcObject as well as src: removeAttribute leaves an attached MediaSource in place, and the
    // next load would then be appending to the source the last song already ended.
    try { video.srcObject = null; } catch { /* ignore */ }
    try { video.removeAttribute('src'); video.load(); } catch { /* ignore */ }
}

// Bumped on every (re)start, so a running fade knows not to tear down what just started.
let playbackGeneration = 0;

async function fadeOutAndStop(fadeMs) {
    const generation = playbackGeneration;

    // A handover that has not swapped yet is silent now and would arrive at full volume part way
    // through the fade, with nothing ramping it: the room hears no fade at all, then the song cut
    // off in one step when this finishes. Dropped first, so there is one thing to fade and it is
    // the thing being heard.
    cancelHandover();

    // Held locally rather than read each tick: a handover that swaps mid-fade would otherwise move
    // the ramp onto the element that just took the room over.
    const element = video;
    const startVolume = element.volume;
    const startedAt = performance.now();

    element.style.transition = `opacity ${fadeMs}ms linear`;
    element.style.opacity = '0';

    // The generation is checked inside the ramp, not only after it: a fade the host has already
    // superseded would otherwise go on pulling the volume down over the song that replaced it.
    const completed = await new Promise((resolve) => {
        const tick = () => {
            if (generation !== playbackGeneration) return resolve(false);

            const progress = Math.min(1, (performance.now() - startedAt) / fadeMs);
            element.volume = startVolume * (1 - progress);

            if (progress < 1) requestAnimationFrame(tick); else resolve(true);
        };
        tick();
    });

    // Superseded: the host started playing again during the fade. The level goes back because the
    // song that replaced this one is using the element, and a ramp abandoned part way leaves it
    // playing into a room that cannot hear it.
    if (!completed) {
        element.volume = currentVolume;
        return;
    }

    teardown();
    video.style.transition = 'opacity 120ms linear';
    video.volume = currentVolume;
    placeholder.hidden = false;
    send({ type: 'state', position: 0, duration: 0, playing: false });
}

// The second channel. No timeline and no correction: only the screen the room hears receives any
// of it, so there is no group for it to stay in step with.
let backgroundVolume = 1;
let backgroundGeneration = 0;

function loadBackground(url, autoplay) {
    backgroundGeneration++;
    background.volume = backgroundVolume;
    background.src = url;
    if (autoplay) background.play().catch((e) => reportError(`bg play: ${e}`));
}

function teardownBackground() {
    try { background.pause(); } catch { /* ignore */ }
    try { background.removeAttribute('src'); background.load(); } catch { /* ignore */ }
}

async function fadeOutBackground(fadeMs) {
    const generation = backgroundGeneration;

    if (fadeMs <= 0) {
        teardownBackground();
        return;
    }

    const startVolume = background.volume;
    const startedAt = performance.now();

    await new Promise((resolve) => {
        const tick = () => {
            const progress = Math.min(1, (performance.now() - startedAt) / fadeMs);
            background.volume = startVolume * (1 - progress);
            if (progress < 1) requestAnimationFrame(tick); else resolve();
        };
        tick();
    });

    // Superseded: a new bed started during the fade, so leave it alone.
    if (generation !== backgroundGeneration) return;

    teardownBackground();
    background.volume = backgroundVolume;
}

// Screens attach at different moments, so each steers onto the host's timeline rather than its
// own start time. Never by trimming playbackRate: that is a pitch error, and WKWebView walks
// currentTime *backwards* when the rate is off 1.0. At 1.0 the drift is ~1ms/s.
const REALIGN_THRESHOLD = 0.15;

// A seek costs a rebuffer, so the drift has to be genuine rather than one noisy sample.
const REALIGN_CONFIRMATIONS = 3;

let clockOffsetMs = 0;
let timeline = null;
let isPrimary = false;

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
    // The primary defines the timeline rather than chasing one, so it is never
    // corrected — there is nothing for it to be corrected towards.
    if (isPrimary) {
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

// setInterval, not rAF: rAF stops while the window is occluded, freezing the correction exactly
// when a screen is most likely to have drifted.
setInterval(correct, 200);

// Pixels per second the band travels when a venue has not chosen. A rate, not a lap time, so a
// long line does not race to keep the same pace as a short one.
const MARQUEE_SPEED = 90;
const MARQUEE_SPEED_MIN = 15;
const MARQUEE_SPEED_MAX = 400;

// What the band last read, so a resend that changes nothing readable (a colour tweak, the same
// queue re-announced after an unrelated change) does not yank the scroll back to its start.
let marqueeSignature = null;

function setMarquee(message) {
    if (message.enabled !== true) {
        marquee.hidden = true;
        marqueeSignature = null;
        return;
    }

    const singers = Array.isArray(message.singers) ? message.singers : [];
    const hasSingers = singers.length > 0;

    // Pinned only means anything while there are names to label; a message-only band pins nothing.
    const pinned = message.pinLabel === true && hasSingers;

    // Nothing to say is not a band across the screen. A venue can leave the message empty and
    // run zero singers, and the room should just see the video.
    if (!hasSingers && !message.message) {
        marquee.hidden = true;
        marqueeSignature = null;
        return;
    }

    // Built as nodes, not markup: a venue types the message and a singer types their own name,
    // and neither may reach innerHTML.
    const build = () => {
        const span = document.createElement('span');

        if (hasSingers) {
            // Held at the edge instead when pinned, so it must not also scroll past.
            if (!pinned) span.appendChild(chip('Up next', 'marquee-label'));

            singers.forEach((name, index) => {
                if (index > 0) span.appendChild(chip('\u2022', 'marquee-sep'));
                span.appendChild(document.createTextNode(name));
            });
        }

        if (message.message) {
            if (hasSingers) span.appendChild(chip('\u2022', 'marquee-sep'));
            span.appendChild(chip(message.message, 'marquee-message'));
        }

        return span;
    };

    // Rebuilt only when what it reads actually changed. Swapping in identical nodes is where the
    // glitch came from: the browser doesn't know the new pair reads the same as the old one, so it
    // restarts the scroll anyway, and one host on top of another restarts the band every few
    // seconds instead of scrolling.
    const signature = JSON.stringify([singers, pinned, message.message || '']);
    const contentChanged = signature !== marqueeSignature;
    marqueeSignature = signature;

    // Both copies carry the same content: the keyframes translate the pair by half its width, so
    // the second is what covers the screen while the first wraps around.
    if (contentChanged) marqueeTrack.replaceChildren(build(), build());

    marquee.dataset.position = message.position === 'top' ? 'top' : 'bottom';
    marquee.dataset.pinned = pinned ? 'true' : 'false';
    marquee.style.setProperty('--marquee-bg', message.backgroundColor || '#000000');
    marquee.style.setProperty('--marquee-fg', message.textColor || '#f2f2f5');

    // Zero means the venue never chose one, so the stylesheet's own size stands. Clamped because
    // the band is fixed to an edge: a size past this covers the picture rather than sitting on it.
    const size = Number(message.fontSizePixels) || 0;
    if (size > 0) marquee.style.setProperty('--marquee-font-size', `${Math.min(96, Math.max(12, size))}px`);
    else marquee.style.removeProperty('--marquee-font-size');

    marquee.hidden = false;

    // Clamped: a speed of zero never finishes a lap, and one past the cap is unreadable.
    const chosen = Number(message.scrollSpeed) || 0;
    const speed = chosen > 0
        ? Math.min(MARQUEE_SPEED_MAX, Math.max(MARQUEE_SPEED_MIN, chosen))
        : MARQUEE_SPEED;

    // Measured after unhiding, or the track has no width to scale the duration against.
    const distance = marqueeTrack.scrollWidth / 2;
    const duration = `${Math.max(4, distance / speed)}s`;
    const durationChanged = duration !== marquee.style.getPropertyValue('--marquee-duration');
    marquee.style.setProperty('--marquee-duration', duration);

    // New or differently-timed text otherwise inherits however far the old lap had already run,
    // so it can appear already partway across the room's screen instead of starting its scroll
    // from the beginning. The animation is declared in the stylesheet, not inline, so clearing it
    // and forcing a reflow before restoring it is what actually restarts its clock.
    if (contentChanged || durationChanged) {
        marqueeTrack.style.animation = 'none';
        void marqueeTrack.offsetWidth;
        marqueeTrack.style.animation = '';
    }
}

function chip(text, className) {
    const el = document.createElement('span');
    el.className = className;
    el.textContent = text;
    return el;
}

function handleCommand(raw) {
    let message;
    try { message = JSON.parse(raw); } catch { return; }

    switch (message.type) {
        case 'load':
            playbackGeneration++;
            placeholder.hidden = false;
            // The old timeline would seek the new stream to a position that means nothing in it.
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

            isPrimary = message.primary === true;

            timeline = next;
            break;
        }
        case 'play':
            playbackGeneration++;
            placeholder.hidden = true;
            // A fade leaves these mid-ramp. Left alone during a handover: the incoming player is
            // deliberately silent and invisible until it has sound to give.
            if (!incoming) {
                video.style.transition = `opacity ${CROSSFADE_MS}ms linear`;
                video.style.opacity = '1';
                video.volume = currentVolume;
            }

            target().play().catch((e) => reportError(`play: ${e}`));
            break;
        case 'pause':
            target().pause();
            break;
        case 'stop':
            timeline = null;
            fadeOutAndStop(Math.max(1, message.fadeMs || 0));
            break;
        case 'seek':
            // Seeking within a stream the page already holds, rather than restarting a transcode.
            try { target().currentTime = message.position || 0; } catch (e) { reportError(`seek: ${e}`); }
            break;
        case 'hostLost':
            hostLost.hidden = message.lost !== true;
            break;
        case 'video':
            // Hidden, not paused: the screen has to keep running to stay on the timeline, and a
            // paused element would drift the moment it was turned back on. visibility, not display:
            // display:none drops the element from the rendering tree, which lets WebKit suspend the
            // decoder and stall on catch-up when the picture comes back.
            videos.forEach((v) => { v.style.visibility = message.enabled === false ? 'hidden' : ''; });
            blanked.hidden = message.enabled !== false;
            break;
        case 'volume':
            currentVolume = Math.max(0, Math.min(1, message.value));
            if (!incoming) video.volume = currentVolume;
            break;
        case 'show-image':
            // The placeholder is the 'nothing here' card, so it goes while a still is up.
            placeholder.hidden = true;
            // A screen is rarely the same shape as the picture, so the host chooses: contain shows
            // all of it, cover fills and crops, fill distorts, none is native pixels centred.
            still.style.objectFit = SCALING[message.scaling] || 'contain';
            still.src = message.url;
            still.hidden = false;
            break;
        case 'hide-image':
            still.hidden = true;
            still.removeAttribute('src');
            break;
        case 'marquee':
            setMarquee(message);
            break;
        case 'bg-load':
            loadBackground(message.url, message.autoplay === true);
            break;
        case 'bg-play':
            backgroundGeneration++;
            background.volume = backgroundVolume;
            background.play().catch((e) => reportError(`bg play: ${e}`));
            break;
        case 'bg-pause':
            background.pause();
            break;
        case 'bg-stop':
            backgroundGeneration++;
            fadeOutBackground(Math.max(0, message.fadeMs || 0));
            break;
        case 'bg-volume':
            backgroundVolume = Math.max(0, Math.min(1, message.value));
            background.volume = backgroundVolume;
            break;
        default:
            break;
    }
}

// The window has no controls, so the page is the only place for these gestures.
document.addEventListener('dblclick', () => send({ type: 'toggle-fullscreen' }));
window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') send({ type: 'exit-fullscreen' });
});

videos.forEach((v) => {
    v.addEventListener('loadeddata', () => { placeholder.hidden = true; });
    // Only from the player the room is hearing: the outgoing one runs out during a handover, and
    // that would retire the singer on the strength of a stream nobody is listening to any more.
    v.addEventListener('ended', () => { if (v === video) send({ type: 'ended' }); });
});

// Its own message, never 'ended': the host runs the singer's performance off that one, and a bed
// track finishing must not retire the song on screen.
background.addEventListener('ended', () => send({ type: 'bg-ended' }));
still.addEventListener('error', () => reportError('still image failed to load'));

background.addEventListener('error', () => {
    const error = background.error;
    if (error) reportError(`background media error ${error.code}`);
});
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
        // Sample time, not send time: guessed latency would bias the timeline forever.
        sampledAtEpochMs: Date.now(),
        // Without this a screen drifting off the group is invisible to the host.
        expected: expected === null ? -1 : expected,
        rate: video.playbackRate,
        readyState: video.readyState,
    });
}, 250);

if (window.external && window.external.receiveMessage) {
    window.external.receiveMessage(handleCommand);
}

// Last line on purpose: everything above is wired, so the host may now send. Until this arrives
// the screen has not told the host it exists — a command pushed into a web view that has no page
// yet takes the whole process down inside Photino's native SendWebMessage, and the page is large
// enough that the gap between the window appearing and this running is real.
send({ type: 'ready' });
