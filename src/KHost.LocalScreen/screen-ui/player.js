// Plays the host's HLS stream through hls.js (demuxes MPEG-TS in JS, feeds MSE). There is no
// native-HLS path, since a web view that can't run hls.js can't serve as a screen anyway.

// Two players, each an element paired with the hls.js instance driving it. `current` is the one
// the room is hearing; `incoming` is one being brought up to speed behind it, so a rebuilt stream
// can take over without the room hearing the join; `outgoing` is one a crossfade is dissolving out.
const videos = [document.getElementById('video'), document.getElementById('video-b')];
let current = { el: videos[0], hls: null };
let incoming = null;
let outgoing = null;

// Set while the song is playing as separate stems mixed here rather than as one stream the host
// mixed. It stands in for the media element everywhere the transport addresses one.
let stemMixer = null;

/// Whichever element is about to be heard. A handover brings the new stream up to speed behind
/// the one still playing, and this is what the transport and the state report both address.
///
/// A stem mix answers here too, shaped enough like an element that none of those callers has to
/// know which kind of song is playing.
function target() { return stemMixer ?? incoming?.el ?? current.el; }

// Where the stream's zero sits in the song, and how fast it runs against it: a stream opened at a
// seek starts at 0 while the song is minutes in, and the words are written against the song.
let songOffsetSeconds = 0;
let songRate = 1;

// The words drawn over the song, when the host sent any. They follow the element's clock rather
// than one of their own, so there is a single clock in the room and they cannot drift from it.
const lyricsCanvas = document.getElementById('lyrics');

/// The song position in seconds, or null when nothing is holding the song.
function songClock() {
    // Below zero while a lead-in holds the song back, so the card and the bar run on across it.
    if (leadIn.active) return songOffsetSeconds - leadIn.remaining;

    const player = target();

    // srcObject as well as src: WebKit refuses hls.js's blob: URL on this opaque-origin page, so
    // the stream is attached as a MediaSource and `src` stays empty. Asking only for `src` reads a
    // playing song as nothing holding it, and the words never draw on macOS at all.
    if (!player || (!player.src && !player.srcObject) || player.readyState < 1) return null;

    return songOffsetSeconds + player.currentTime * songRate;
}

const overlay = createLyricsOverlay(lyricsCanvas, songClock);

// The card heading the words, on the words' own clock. Its layer is what a stop fades, since the
// card sets its own opacity from the clock.
const introLayer = document.getElementById('intro-layer');
const introCard = createIntroCard(introLayer, songClock);

// Runs out into the song: the element or the mix is started only once the hold is over.
const leadIn = createLeadInHold(() => target().play().catch((e) => reportError(`play: ${e}`)));

// The words as the host sent them, kept so the lead-in's bar can be put on and taken off again.
let words = { lyrics: null, intro: null, leadInSeconds: 0, led: false };

/// Hands the words to the overlay and the card, with the lead-in's bar on them or without.
function showLyrics(led) {
    words.led = led && words.leadInSeconds > 0;

    const lyrics = words.led ? withLeadInCountIn(words.lyrics, words.leadInSeconds) : words.lyrics;
    overlay.setLyrics(lyrics);
    // Only ever beside words: the host sends none for a picture that carries its own.
    introCard.set(words.intro, lyrics, words.led ? words.leadInSeconds : 0);
}

/// Nothing of the song has been heard yet: a stream opened at zero that has not moved.
function atSongStart() {
    const player = target();
    return songOffsetSeconds === 0 && !(Number(player && player.currentTime) > 0.05);
}

// Everything drawn over the song rather than streamed: a stop dims these with the sound.
const songLayers = [lyricsCanvas, introLayer];
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

function drawCorners() {
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

/// A handover that never becomes ready must not strand the change; take it anyway.
const HANDOVER_TIMEOUT_MS = 4000;
const CROSSFADE_MS = 120;

// A decode glitch can usually be recovered in place, but a source that never decodes would
// otherwise recover forever, so give up and let the host hear about it.
const MAX_MEDIA_RECOVERIES = 2;

/// Drops a handover that has not swapped yet, leaving whatever is playing alone.
function cancelHandover() {
    if (!incoming) return;

    destroyHls(incoming.hls);
    retire(incoming.el);
    incoming = null;
}

/// Lets go of the player a crossfade is dissolving out, early when something needs its element.
function dropOutgoing() {
    if (!outgoing) return;

    destroyHls(outgoing.hls);
    retire(outgoing.el);
    outgoing = null;
}

function detachHls() {
    destroyHls(current.hls);
    current.hls = null;

    // A handover still in flight has to go with it, or its element keeps decoding into nothing.
    cancelHandover();
    dropOutgoing();
}

/// One per instance. An instance starts as the incoming half of a handover and becomes the playing
/// one on the swap, so which it is has to be asked when the error arrives, not when it is wired.
function hlsErrorHandler(instance) {
    let mediaRecoveries = 0;

    return (_, data) => {
        if (!data.fatal) return;

        // A replacement that fails is dropped, and the stream the room is hearing stays up.
        if (incoming && instance === incoming.hls) {
            reportError(`hls (handover): ${data.details}`);
            cancelHandover();
            return;
        }

        // Retired or replaced: nothing it drives is being heard any more.
        if (instance !== current.hls) return;

        if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
            instance.startLoad();
            return;
        }

        if (data.type === Hls.ErrorTypes.MEDIA_ERROR && mediaRecoveries < MAX_MEDIA_RECOVERIES) {
            mediaRecoveries++;
            instance.recoverMediaError();
            return;
        }

        reportError(`hls: ${data.details}`);
        detachHls();
    };
}

function load(url, autoplay, pixelated) {
    // Nothing to hand over from: a stopped or unstarted player takes the stream directly, which
    // is the path every fresh song uses and the one that has always worked.
    if (!current.hls || current.el.paused || current.el.readyState < 3) {
        setPixelated(current.el, pixelated);
        reveal(current.el);

        detachHls();
        attach(current, url, autoplay);
        return;
    }

    // Something is playing. Bring the replacement up behind it silently, and only swap once it
    // has sound to give. Tearing the old one down first is the gap this exists to remove.
    //
    // A pending handover is superseded rather than raced, and the only free element may be one a
    // crossfade is still dissolving out, which would otherwise retire it under this stream.
    cancelHandover();
    dropOutgoing();
    // Dropping the crossfade also stops the ramp that was bringing the current player up.
    current.el.volume = currentVolume;

    const arriving = { el: videos.find((v) => v !== current.el), hls: null };

    retire(arriving.el);
    setPixelated(arriving.el, pixelated);
    incoming = arriving;

    arriving.el.volume = 0;
    arriving.el.style.transition = 'none';
    arriving.el.style.opacity = '0';

    // No-ops once superseded: handOver swaps only the pair still in `incoming`, and a later load
    // makes a new pair even on the same element.
    const swap = () => {
        clearTimeout(timer);
        handOver(arriving);
    };

    const timer = setTimeout(swap, HANDOVER_TIMEOUT_MS);

    arriving.el.addEventListener('playing', swap, { once: true });

    attach(arriving, url, true);
}

/// Block graphics scaled without smoothing, on the element that is about to show them. Set on every
/// load, so a video that follows a CD+G onto the same element is smoothed again.
function setPixelated(el, pixelated) {
    el.classList.toggle('video--pixelated', pixelated === true);
}

/// Wires one player to a stream. The engine dance below is why this is shared rather than copied.
function attach(player, url, autoplay) {
    if (!window.Hls || !Hls.isSupported()) {
        reportError('this webview cannot run hls.js: no Media Source Extensions');
        return;
    }

    const el = player.el;
    const instance = new Hls({ preferManagedMediaSource: false });
    player.hls = instance;

    instance.on(Hls.Events.ERROR, hlsErrorHandler(instance));
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
function handOver(arriving) {
    if (incoming !== arriving) return;

    const leaving = current;
    incoming = null;
    current = arriving;
    outgoing = leaving;

    arriving.el.style.transition = `opacity ${CROSSFADE_MS}ms linear`;
    arriving.el.style.opacity = '1';
    leaving.el.style.transition = `opacity ${CROSSFADE_MS}ms linear`;
    leaving.el.style.opacity = '0';

    // Once dropped, the leaving element may already be carrying the next stream, so neither ramp
    // nor the retire may touch it again.
    const stillCrossfading = () => outgoing === leaving;

    Promise.all([
        rampVolume(arriving.el, 0, currentVolume, CROSSFADE_MS, stillCrossfading),
        rampVolume(leaving.el, currentVolume, 0, CROSSFADE_MS, stillCrossfading),
    ]).then(() => {
        if (outgoing === leaving) dropOutgoing();
    });
}

/// Moves an element's volume from one level to another, resolving false and leaving the level
/// where it got to as soon as stillCurrent() says the ramp has been superseded.
///
/// A timer, not rAF: rAF stops while the window is occluded, and a stop fade that never finishes
/// never stops the song.
function rampVolume(el, from, to, ms, stillCurrent) {
    return new Promise((resolve) => {
        const startedAt = performance.now();
        let timer = null;

        const step = () => {
            if (!stillCurrent()) {
                clearInterval(timer);
                resolve(false);
                return true;
            }

            const progress = ms > 0 ? Math.min(1, (performance.now() - startedAt) / ms) : 1;
            try { el.volume = from + (to - from) * progress; } catch { /* detached mid-ramp */ }

            if (progress < 1) return false;

            clearInterval(timer);
            resolve(true);
            return true;
        };

        if (!step()) timer = setInterval(step, 16);
    });
}

/// Waits ms, resolving false early once stillCurrent() says the wait has been superseded.
/// A timer for the same reason rampVolume uses one.
function waitUnlessSuperseded(ms, stillCurrent) {
    return new Promise((resolve) => {
        const endsAt = performance.now() + ms;

        const timer = setInterval(() => {
            if (!stillCurrent()) { clearInterval(timer); resolve(false); return; }
            if (performance.now() >= endsAt) { clearInterval(timer); resolve(true); }
        }, 16);
    });
}

/// Stops an element and lets go of its source, without touching whatever is playing.
function retire(el) {
    try { el.pause(); } catch { /* ignore */ }
    // srcObject as well as src: removeAttribute leaves an attached MediaSource in place, and the
    // next load would then be appending to the source the last song already ended.
    try { el.srcObject = null; } catch { /* ignore */ }
    try { el.removeAttribute('src'); el.load(); } catch { /* ignore */ }
}

function destroyHls(instance) {
    if (!instance) return;

    try { instance.destroy(); } catch { /* ignore */ }
}

function detachStems() {
    if (!stemMixer) return;

    try { stemMixer.destroy(); } catch (e) { reportError(`stems: ${e}`); }
    stemMixer = null;
}

function teardown() {
    // Before the element is cleared: destroy() detaches the media it is driving.
    detachHls();
    detachStems();
    retire(current.el);
}

/// Puts the words back at full view at once, which a stop's fade leaves part way or gone.
function showWords() {
    for (const layer of songLayers) {
        layer.style.transition = 'none';
        layer.style.opacity = '1';
    }
}

/// Brings a player back to full view at the room's level, which a fade leaves part way.
function reveal(el) {
    el.style.transition = `opacity ${CROSSFADE_MS}ms linear`;
    el.style.opacity = '1';
    el.volume = currentVolume;
}

// Bumped on every (re)start, so a running fade knows not to tear down what just started.
let playbackGeneration = 0;

async function fadeOutAndStop(fadeMs) {
    const generation = playbackGeneration;

    // A handover that hasn't swapped yet is silent now and would arrive at full volume mid-fade with
    // nothing ramping it. Dropped first, so there is one thing to fade and it is the thing being heard.
    cancelHandover();
    // A crossfade still bringing the current player up would pull against the fade.
    dropOutgoing();

    // Held locally rather than read each tick: a handover that swaps mid-fade would otherwise move
    // the ramp onto the element that just took the room over.
    const element = current.el;
    // A stem song is heard through the mixer, not the element, which sits idle and silent.
    const mixer = stemMixer;

    element.style.transition = `opacity ${fadeMs}ms linear`;
    element.style.opacity = '0';
    // The words are a stem song's whole picture, its element sitting empty, so they dim with the sound.
    for (const layer of songLayers) {
        layer.style.transition = `opacity ${fadeMs}ms linear`;
        layer.style.opacity = '0';
    }

    // The generation is checked inside the ramp, not only after it: a fade the host has already
    // superseded would otherwise go on pulling the volume down over the song that replaced it.
    const stillCurrent = () => generation === playbackGeneration;
    let completed;

    if (mixer) {
        mixer.fadeOut(fadeMs);
        completed = await waitUnlessSuperseded(fadeMs, stillCurrent);
    } else {
        completed = await rampVolume(element, element.volume, 0, fadeMs, stillCurrent);
    }

    // Superseded: the host started playing again during the fade, and the song that replaced this
    // one is using the element now. A ramp abandoned part way would leave it playing unheard.
    if (!completed) {
        element.volume = currentVolume;
        if (mixer && mixer === stemMixer) mixer.volume = currentVolume;
        return;
    }

    teardown();
    leadIn.cancel();
    reveal(current.el);
    // Cleared before it is shown again, or the frame the fade hid comes back for a moment.
    overlay.clear();
    showWords();
    placeholder.hidden = false;
    send({ type: 'state', position: 0, duration: 0, playing: false });
}

// The second channel, for break music and an ad's bed. It has no song position of its own.
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

    // Checked inside the ramp: a bed loaded or resumed during the fade would otherwise be pulled
    // down to nothing and left there.
    const completed = await rampVolume(
        background, background.volume, 0, fadeMs, () => generation === backgroundGeneration);

    // Superseded: a new bed started during the fade, so leave it alone.
    if (!completed) return;

    teardownBackground();
    background.volume = backgroundVolume;
}

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
        drawCorners();
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

    drawCorners();
}

function setQrCodes(message) {
    const codes = Array.isArray(message.codes) ? message.codes : [];

    // At most one is ever drawn, since the venue names the source, so the first is the whole of it.
    const code = codes.find((entry) => entry && entry.imageUrl);

    if (!code) {
        cornerItems.qr = null;
        drawCorners();
        return;
    }

    setCornerOffset(code.offset);

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

    drawCorners();
}

// Kind -> the class that colours it; 'other' and 'separator' are handled separately below.
const MARQUEE_SEGMENT_CLASSES = { singer: 'marquee-singer', song: 'marquee-song' };

function setMarquee(message) {
    if (message.enabled !== true) {
        marquee.hidden = true;
        marqueeSignature = null;
        return;
    }

    const entries = Array.isArray(message.entries) ? message.entries : [];
    const hasEntries = entries.length > 0;
    const glyph = message.dividerGlyph || '';

    // Pinned only means anything while there are names to label; a message-only band pins nothing.
    const pinned = message.pinLabel === true && hasEntries;

    // Nothing to say is not a band across the screen. A venue can leave the message empty and
    // run zero singers, and the room should just see the video.
    if (!hasEntries && !message.message) {
        marquee.hidden = true;
        marqueeSignature = null;
        return;
    }

    // A divider's own colour and glyph, so the pre-message one matches the ones between entries.
    const dividerChip = () => chip(glyph, 'marquee-sep');

    // Built as nodes, not markup: a venue types the message and a singer types their own name,
    // and neither may reach innerHTML.
    const build = () => {
        const span = document.createElement('span');

        if (hasEntries) {
            // Held at the edge instead when pinned, so it must not also scroll past.
            if (!pinned) span.appendChild(chip('Up next', 'marquee-label'));

            entries.forEach((segment) => {
                if (segment.kind === 'separator') {
                    if (glyph) span.appendChild(dividerChip());
                    return;
                }

                const className = MARQUEE_SEGMENT_CLASSES[segment.kind];
                span.appendChild(className ? chip(segment.text, className) : document.createTextNode(segment.text));
            });
        }

        if (message.message) {
            if (hasEntries && glyph) span.appendChild(dividerChip());
            span.appendChild(chip(message.message, 'marquee-message'));
        }

        return span;
    };

    // Rebuilt only when what it reads actually changed: swapping in identical nodes still restarts
    // the scroll. That showed as the band restarting every few seconds instead of scrolling. The
    // glyph is in here too: a shape-only edit leaves entries untouched but redraws a different
    // character into the same separator nodes.
    const signature = JSON.stringify([entries, pinned, message.message || '', glyph]);
    const contentChanged = signature !== marqueeSignature;
    marqueeSignature = signature;

    // Every copy carries the same content: the keyframes translate the track by exactly one of
    // them, so the next sits where the last began and the join never shows.
    if (contentChanged) marqueeTrack.replaceChildren(build(), build());

    marquee.dataset.position = message.position === 'top' ? 'top' : 'bottom';
    marquee.dataset.pinned = pinned ? 'true' : 'false';
    marquee.style.setProperty('--marquee-bg', message.backgroundColor || '#000000');
    marquee.style.setProperty('--marquee-fg', message.textColor || '#f2f2f5');

    // Unset takes the CSS default (today's look); each is independent of the others.
    if (message.singerColor) marquee.style.setProperty('--marquee-singer-fg', message.singerColor);
    else marquee.style.removeProperty('--marquee-singer-fg');

    if (message.songColor) marquee.style.setProperty('--marquee-song-fg', message.songColor);
    else marquee.style.removeProperty('--marquee-song-fg');

    // A chosen divider colour draws at full strength; the default currentColor is what carries the
    // dimming instead (see the stylesheet), so an explicit colour is not washed out by it too.
    if (message.dividerColor) {
        marquee.style.setProperty('--marquee-sep-fg', message.dividerColor);
        marquee.style.setProperty('--marquee-sep-opacity', '1');
    } else {
        marquee.style.removeProperty('--marquee-sep-fg');
        marquee.style.removeProperty('--marquee-sep-opacity');
    }

    // Null/undefined means never chosen, not zero: zero is a real, fully-transparent choice.
    const opacity = message.backgroundOpacityPercent;
    if (typeof opacity === 'number' && Number.isFinite(opacity))
        marquee.style.setProperty('--marquee-bg-opacity', String(Math.min(100, Math.max(0, opacity)) / 100));
    else
        marquee.style.removeProperty('--marquee-bg-opacity');

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

    // Taking the screen back: a song starting, or the picture being set. Deliberately not marquee
    // or codes, which would cancel an announcement the host had only just made.
    if (['load', 'play', 'stop', 'show-image', 'hide-image'].includes(message.type)) clearNextSinger();

    switch (message.type) {
        case 'load':
            playbackGeneration++;
            showWords();
            placeholder.hidden = false;
            // Where this stream sits in the song. A rebuild after a seek sends new values, and the
            // words are drawn against the song, so they have to move with it.
            songOffsetSeconds = message.songOffsetSeconds || 0;
            songRate = message.rate || 1;

            // A stream reopened under a running hold (a key change at the top) keeps the hold;
            // one opened part way in is a song already under way.
            if (songOffsetSeconds > 0 && !leadIn.active) leadIn.cancel();

            // Stems arrive unmixed and are mixed here, so moving a voice later costs a gain rather
            // than a new encode. The stream URL is still sent beside them, and is what a page that
            // could not mix would have played instead.
            if (message.stems && message.stems.length > 0) {
                teardown();
                stemMixer = createStemMixer(message.stems, songOffsetSeconds, reportError, currentVolume);
                if (message.autoplay === true) {
                    stemMixer.play().catch((e) => reportError(`stem play: ${e}`));
                }
                break;
            }

            detachStems();
            load(message.url, message.autoplay === true, message.pixelated === true);
            break;
        case 'stem-volume': {
            // Silently doing nothing would look exactly like a mix that has stopped responding.
            if (!stemMixer) { reportError('stem-volume with no stems playing'); break; }

            const moved = stemMixer.setStemVolume(message.role, message.voice ?? null, message.volume || 0);
            if (moved === 0) reportError(`stem-volume for ${message.role} ${message.voice ?? ''}, which this song has none of`);
            break;
        }
        case 'timed-lyrics':
            // The whole timing document, sent once with the load rather than on the transport.
            // Null clears it, which is what a song with no words looks like.
            words = {
                lyrics: message.lyrics || null,
                intro: message.intro || null,
                leadInSeconds: Number(message.leadInSeconds) || 0,
                led: false,
            };
            showLyrics(false);

            // Words arrive once per song, so whatever hold the last one left goes. Armed only ahead
            // of a page, since with none there is nothing to lead the singer in to; the play that
            // starts it checks the song is still at its top.
            leadIn.cancel();
            if (firstPageAt(words.lyrics) !== null) leadIn.arm(words.leadInSeconds);
            break;
        case 'play':
            playbackGeneration++;
            showWords();
            placeholder.hidden = true;
            // A fade leaves these mid-ramp. Left alone during a handover: the incoming player is
            // deliberately silent and invisible until it has sound to give.
            if (!incoming) reveal(current.el);
            if (stemMixer) stemMixer.volume = currentVolume;

            if (leadIn.active) {
                leadIn.resume();
                break;
            }

            if (leadIn.armed && atSongStart()) {
                showLyrics(true);
                leadIn.start();
                break;
            }

            // Once only: a later resume from the top is not a song starting.
            leadIn.cancel();
            target().play().catch((e) => reportError(`play: ${e}`));
            break;
        case 'pause':
            leadIn.pause();
            target().pause();
            break;
        case 'stop':
            // Paused rather than dropped: a play superseding the fade picks it back up, and a
            // fade that completes cancels it with everything else.
            leadIn.pause();
            fadeOutAndStop(Math.max(1, message.fadeMs || 0));
            break;
        case 'seek': {
            // The host chose a place in the song, so the rest of any hold goes and the song
            // starts there, running if the hold was.
            const holdWasRunning = leadIn.active && !leadIn.paused;
            leadIn.cancel();
            if (words.led) showLyrics(false);

            // Seeking within a stream the page already holds, rather than restarting an encode.
            try { target().currentTime = message.position || 0; } catch (e) { reportError(`seek: ${e}`); }

            if (holdWasRunning) target().play().catch((e) => reportError(`play: ${e}`));
            break;
        }
        case 'hostLost':
            hostLost.hidden = message.lost !== true;
            break;
        case 'video':
            // Hidden, not paused: a paused element would drift the moment it's turned back on.
            // visibility, not display: display:none drops it from the render tree, stalling WebKit's decoder.
            videos.forEach((v) => { v.style.visibility = message.enabled === false ? 'hidden' : ''; });
            // The words and their card hide the same way, and for the same reason: the engine keeps drawing so
            // the words are still on the song when the picture comes back.
            for (const layer of songLayers) layer.style.visibility = message.enabled === false ? 'hidden' : '';
            blanked.hidden = message.enabled !== false;
            break;
        case 'volume':
            currentVolume = Math.max(0, Math.min(1, message.value));
            // The venue's level rides the whole mix, not one stem: it is the room's volume, and
            // the stems' own levels are what the host set them to against each other.
            if (stemMixer) stemMixer.volume = currentVolume;
            if (!incoming) current.el.volume = currentVolume;
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
    v.addEventListener('ended', () => { if (v === current.el) send({ type: 'ended' }); });
    v.addEventListener('error', () => {
        if (v !== current.el) return;

        const error = v.error;
        if (error) reportError(`media error ${error.code}`);
    });
});

// Its own message, never 'ended': the host runs the singer's performance off that one, and a bed
// track finishing must not retire the song on screen.
background.addEventListener('ended', () => send({ type: 'bg-ended' }));
still.addEventListener('error', () => reportError('still image failed to load'));

background.addEventListener('error', () => {
    const error = background.error;
    if (error) reportError(`background media error ${error.code}`);
});

/// A context that lived through a sleep can keep its clock running and play nothing, so a stem
/// mix is rebuilt on a fresh one. An element's stream is left alone: each load gets its own.
async function rebuildStemsAfterWake() {
    const old = stemMixer;
    const create = (stems) => createStemMixer(stems, songOffsetSeconds, reportError, currentVolume);

    try {
        const rebuilt = await rebuildStemMixer(old, create, () => stemMixer === old);
        if (!rebuilt) return;

        stemMixer = rebuilt.mixer;
        if (rebuilt.playing) await stemMixer.play();

        send({
            type: 'audio-rebuilt',
            position: rebuilt.position,
            playing: rebuilt.playing,
            audioState: stemMixer.audioState,
        });
    } catch (e) {
        reportError(`stems after wake: ${e}`);
    }
}

const watchForWake = createWakeWatch((asleepMs) => {
    send({
        type: 'wake',
        asleepSeconds: Math.round(asleepMs / 1000),
        holding: stemMixer ? 'stems' : current.hls ? 'stream' : 'nothing',
    });

    if (stemMixer) rebuildStemsAfterWake();
});

// The host polls nothing; position reaches it only through these reports.
setInterval(() => {
    watchForWake();

    // The engine when it holds the song: reporting the idle video element's zero would move the
    // host's playhead back to the start of a song that is still playing.
    const player = target();

    // A hold is the song at zero, playing: the host's playhead sits at the start rather than
    // running on through the hold and jumping back when the song starts.
    const holding = leadIn.active;

    send({
        type: 'state',
        position: holding ? 0 : Number.isFinite(player.currentTime) ? player.currentTime : 0,
        duration: Number.isFinite(player.duration) ? player.duration : 0,
        playing: holding ? !leadIn.paused : !player.paused && !player.ended && player.readyState > 2,
        // Sample time, not send time: guessed latency would bias the host's playhead forever.
        sampledAtEpochMs: Date.now(),
        rate: player.playbackRate,
        readyState: player.readyState,
        // A stem mix's context state; undefined for an element, so it drops out of the JSON.
        audioState: player.audioState,
    });
}, 250);

if (window.external && window.external.receiveMessage) {
    window.external.receiveMessage(handleCommand);
}

// Last line on purpose: until this arrives the host doesn't know the screen exists, and a command
// pushed into a page-less web view crashes the whole process inside Photino's SendWebMessage.
send({ type: 'ready' });
