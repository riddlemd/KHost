// Plays the host's HLS stream through hls.js (demuxes MPEG-TS in JS, feeds MSE). There is no
// native-HLS path, since a web view that can't run hls.js can't serve as a screen anyway.

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
const nextSinger = document.getElementById('nextSinger');
const blanked = document.getElementById('blanked');
const hostLost = document.getElementById('hostlost');
const marquee = document.getElementById('marquee');
const marqueeTrack = document.getElementById('marquee-track');
const marqueeViewport = document.getElementById('marquee-viewport');
const marqueePin = document.getElementById('marquee-pin');
const corners = new Map(
    [...document.querySelectorAll('.kh-corner')].map((el) => [el.dataset.corner, el]));

// What each owner last put in a corner, so a corner can be rebuilt without the other's command.
// The card sits above the code, which keeps the pair from swapping places as each comes and goes.
const cornerItems = { breakMusic: null, qr: null };

function renderCorners() {
    for (const el of corners.values()) el.replaceChildren();

    for (const key of ['breakMusic', 'qr']) {
        const item = cornerItems[key];
        if (!item) continue;

        const corner = corners.get(item.corner);
        if (corner) corner.appendChild(item.node);
    }
}

// The inset belongs to the corner, so the last command carrying one sets it for every corner.
// Two things stacked against one edge have to agree on how far in it is.
function setCornerOffset(offset) {
    if (!Number.isFinite(offset) || offset <= 0) return;

    document.documentElement.style.setProperty('--kh-corner-offset', `${offset}vmin`);
}

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
    // has sound to give. Tearing the old one down first is the gap this exists to remove.
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

    // WebKit refuses hls.js's blob: URL on this opaque-origin page, so this uses srcObject; Chromium's
    // srcObject throws on a bare MediaSource, so it falls back to attachMedia in a try (else silent).
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

    // A handover that hasn't swapped yet is silent now and would arrive at full volume mid-fade with
    // nothing ramping it. Dropped first, so there is one thing to fade and it is the thing being heard.
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

    // Superseded: the host started playing again during the fade, and the song that replaced this
    // one is using the element now. A ramp abandoned part way would leave it playing unheard.
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

// Screens attach at different moments, so each steers onto the host's timeline rather than its own
// start time, never via playbackRate: that pitches audio, and WKWebView walks currentTime backwards.
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
    // The primary defines the timeline rather than chasing one, so it is never corrected.
    // There is nothing for it to be corrected towards.
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

// A ceiling on the tiling. A one-word band on a wide screen would otherwise ask for dozens of
// copies, and past a point the band is full either way. What it buys is nodes, not smoothness.
const MARQUEE_COPIES_MAX = 12;

// What the band last read, so a resend that changes nothing readable (a colour tweak, the same
// queue re-announced after an unrelated change) does not yank the scroll back to its start.
let marqueeSignature = null;

const QR_CORNERS = ['bottomright', 'bottomleft', 'topright', 'topleft'];
const QR_SIZES = ['small', 'medium', 'large'];

// What is playing between singers. Text only: a title and an artist off a provider, which is
// exactly why it is built as nodes rather than markup.
/// Names who is up, over the whole picture. Nothing here takes it down: the next thing drawn does.
function showNextSinger(message) {
    nextSinger.querySelector('.kh-next__singer').textContent = message.singer || '';

    const song = nextSinger.querySelector('.kh-next__song');
    // A singer on the list with nothing queued is named alone rather than under an empty line.
    song.textContent = message.artist ? `${message.song} - ${message.artist}` : (message.song || '');
    song.hidden = !message.song;

    // The venue's card and any still are what this replaces, so both go while it is up.
    still.hidden = true;
    placeholder.hidden = true;
    nextSinger.hidden = false;
}

/// Anything that redraws the picture clears the card. Deliberately not every command: a marquee or
/// a code update is not somebody taking the screen back, and would cancel an announcement the host
/// had only just made.
function clearNextSinger() {
    if (nextSinger.hidden) return;

    nextSinger.hidden = true;
    placeholder.hidden = !still.hidden;
}

function setBreakMusicCard(message) {
    if (!message.enabled || !message.title) {
        cornerItems.breakMusic = null;
        renderCorners();
        return;
    }

    setCornerOffset(message.offset);

    const card = document.createElement('div');
    card.className = 'kh-break-music';

    const title = document.createElement('span');
    title.className = 'kh-break-music__title';
    title.textContent = message.title;
    card.appendChild(title);

    if (message.artist) {
        const artist = document.createElement('span');
        artist.className = 'kh-break-music__artist';
        artist.textContent = message.artist;
        card.appendChild(artist);
    }

    cornerItems.breakMusic = {
        corner: QR_CORNERS.includes(message.corner) ? message.corner : 'bottomleft',
        node: card,
    };

    renderCorners();
}

function setQrCodes(message) {
    const codes = Array.isArray(message.codes) ? message.codes : [];

    // At most one is ever drawn, since the venue names the source, so the first is the whole of it.
    const code = codes.find((entry) => entry && entry.imageUrl);

    if (!code) {
        cornerItems.qr = null;
        renderCorners();
        return;
    }

    setCornerOffset(code.offset);

    {
        const figure = document.createElement('figure');
        figure.className = 'kh-qr';
        figure.dataset.size = QR_SIZES.includes(code.size) ? code.size : 'medium';

        // A denser code drawn in the same corner has smaller modules; below about three pixels each
        // no phone reads it, so the module count sets a floor the venue's size cannot go under.
        if (Number.isFinite(code.modules) && code.modules > 0)
            figure.style.setProperty('--kh-qr-modules', String(code.modules));

        // Already resolved host-side from the venue, so nothing here decides a default: a screen
        // guessing one is how two screens in a room end up disagreeing.
        if (Number.isFinite(code.safeZone) && code.safeZone > 0)
            figure.style.setProperty('--kh-qr-safezone', String(code.safeZone));

        const image = document.createElement('img');
        // Decorative in the accessibility sense: nobody is reading a karaoke screen with a
        // reader, and a code carries its meaning by being one.
        image.alt = '';
        image.src = code.imageUrl;
        figure.appendChild(image);

        if (code.caption) {
            const caption = document.createElement('figcaption');
            caption.textContent = code.caption;
            figure.appendChild(caption);
        }

        cornerItems.qr = {
            corner: QR_CORNERS.includes(code.corner) ? code.corner : 'bottomright',
            node: figure,
        };
    }

    renderCorners();
}

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

    // Rebuilt only when what it reads actually changed: swapping in identical nodes still restarts
    // the scroll. That showed as the band restarting every few seconds instead of scrolling.
    const signature = JSON.stringify([singers, pinned, message.message || '']);
    const contentChanged = signature !== marqueeSignature;
    marqueeSignature = signature;

    // Every copy carries the same content: the keyframes translate the track by exactly one of
    // them, so the next sits where the last began and the join never shows.
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

    // Measured after unhiding, or the track has no width to measure. Enough copies to cover the
    // screen plus one spare; a fixed two left a gap on a full-screen band with a short line.
    const copies = copiesToCoverTheBand();
    marquee.style.setProperty('--marquee-copies', String(copies));

    // One copy's width: the distance a lap actually travels, which is what the venue's speed is
    // in pixels a second of. Speed, not a lap time: a fixed duration would make a long line race
    // to keep a short one's pace.
    const distance = marqueeTrack.scrollWidth / copies;
    const duration = `${Math.max(4, distance / speed)}s`;
    const durationChanged = duration !== marquee.style.getPropertyValue('--marquee-duration');
    marquee.style.setProperty('--marquee-duration', duration);

    // Otherwise new or differently-timed text inherits however far the old lap had already run.
    // The animation lives in the stylesheet; clearing it and forcing a reflow restarts its clock.
    if (contentChanged || durationChanged) {
        marqueeTrack.style.animation = 'none';
        void marqueeTrack.offsetWidth;
        marqueeTrack.style.animation = '';
    }
}

/// Re-tiles the track so one copy's width is never less than the band it has to cross, returns
/// how many there are. One copy is window-wide, a fraction of one on a television.
function copiesToCoverTheBand() {
    const first = marqueeTrack.firstElementChild;

    if (!first) return 2;

    const copyWidth = first.getBoundingClientRect().width;
    const band = marqueeViewport?.clientWidth || marquee.clientWidth;

    // A copy with no width yet, or a band with none, is nothing to divide by. Two is the old
    // behaviour and is right as soon as one copy is wider than the band anyway.
    if (!(copyWidth > 0) || !(band > 0)) return 2;

    // One to cover the band, one more to be arriving while it does.
    const wanted = Math.min(MARQUEE_COPIES_MAX, Math.ceil(band / copyWidth) + 1);

    if (wanted === marqueeTrack.childElementCount) return wanted;

    const template = first.cloneNode(true);
    const copies = [];

    for (let i = 0; i < wanted; i++) copies.push(template.cloneNode(true));

    marqueeTrack.replaceChildren(...copies);

    return wanted;
}

// The band's width is not fixed: a screen going full screen or a resized window leaves the
// copies that covered it a moment ago covering only a fraction. This is the only re-measure.
function retileMarquee() {
    if (marquee.hidden || !marqueeTrack.firstElementChild) return;

    marquee.style.setProperty('--marquee-copies', String(copiesToCoverTheBand()));
}

if (marqueeViewport && typeof ResizeObserver === 'function') {
    let pending = 0;

    // Debounced: a drag reports every frame, and re-tiling replaces the track's children, which
    // restarts the scroll. Once the drag settles is the only time worth doing that.
    const observer = new ResizeObserver(() => {
        clearTimeout(pending);
        pending = setTimeout(retileMarquee, 150);
    });

    observer.observe(marqueeViewport);
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

    // Taking the screen back: a song starting, or the picture being set. Deliberately not marquee,
    // codes or a timeline tick, which would cancel an announcement the host had only just made.
    if (['load', 'play', 'stop', 'show-image', 'hide-image'].includes(message.type)) clearNextSinger();

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
            // Hidden, not paused: a paused element would drift the moment it's turned back on.
            // visibility, not display: display:none drops it from the render tree, stalling WebKit's decoder.
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
            // The card is what an empty screen looks like, so it comes back as the still goes.
            // Every other path that hides the still restores it; leaving it out here meant a venue
            // clearing its picture got a black screen with nothing on it at all, since show-image
            // had already hidden the card and nothing put it back.
            placeholder.hidden = false;
            break;
        case 'marquee':
            setMarquee(message);
            break;
        case 'qr-codes':
            setQrCodes(message);
            break;
        case 'break-music-card':
            setBreakMusicCard(message);
            break;
        case 'next-singer':
            showNextSinger(message);
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

// Last line on purpose: until this arrives the host doesn't know the screen exists, and a command
// pushed into a page-less web view crashes the whole process inside Photino's SendWebMessage.
send({ type: 'ready' });
