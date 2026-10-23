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

    // A mix's time is arithmetic, valid before its stems decode. Waiting on readyState instead
    // blanks the words and card for as long as a decode outlasts the lead-in.
    if (player && player === stemMixer) return songOffsetSeconds + player.currentTime * songRate;

    // srcObject as well as src: WebKit refuses hls.js's blob: URL on this opaque-origin page, so
    // the stream is attached as a MediaSource and `src` stays empty. Asking only for `src` reads a
    // playing song as nothing holding it, and the words never draw on macOS at all.
    if (!player || (!player.src && !player.srcObject) || player.readyState < 1) return null;

    return songOffsetSeconds + player.currentTime * songRate;
}

const overlay = createLyricsOverlay(lyricsCanvas, songClock);

const titleBar = createTitleBar({ doc: document, send, requestFrame: (f) => requestAnimationFrame(f) });

// The card heading the words, on the words' own clock. Its layer is what a stop fades, since the
// card sets its own opacity from the clock.
const introLayer = document.getElementById('intro-layer');
const introCard = createIntroCard(introLayer, songClock);

// Runs out into the song: the element or the mix is started only once the hold is over.
// Reported at once either way, so the host's clock holds with it rather than a report later.
const leadIn = createLeadInHold(() => {
    target().play().catch((e) => reportError(`play: ${e}`));
    reportState();
});

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
    return songOffsetSeconds === 0 && !(Number(player && player.currentTime) > SONG_START_TOLERANCE_SECONDS);
}

// Under the words, for a song with nothing of its own to show there; the host says when.
const visualiserCanvas = document.getElementById('visualiser');
const visualiserEqCanvas = document.getElementById('visualiser-eq');
const visualiserVideo = document.getElementById('visualiser-video');
const visualiser = createVisualiser(visualiserCanvas, {
    eqCanvas: visualiserEqCanvas,
    videoEl: visualiserVideo,
    engine: window.butterchurn && window.butterchurn.default,
    presets: VISUALISER_PRESETS,
    reportError,
    // The words' clock, so a seek, a rebuild's offset and a tempo all land where the words do.
    clock: songClock,
});

// The host's levels for the song, fetched once per URL. A fetch waits for the host's read, so a
// newer URL (or none) arriving meanwhile must win over the answer to an older one.
let visualiserLevelsUrl = null;

function loadVisualiserLevels(url) {
    if (url === visualiserLevelsUrl) return;

    visualiserLevelsUrl = url || null;
    visualiser.setLevels(null);
    if (!visualiserLevelsUrl) return;

    fetch(url)
        .then((response) => (response.ok ? response.arrayBuffer() : null))
        .then((buffer) => {
            if (url !== visualiserLevelsUrl) return;

            const track = buffer ? parseVisualiserLevels(buffer) : null;
            // Without them the picture still moves, just not to the song.
            if (!track) { reportError('visualiser levels: none for this song'); return; }

            visualiser.setLevels(track);
        })
        .catch((e) => { if (url === visualiserLevelsUrl) reportError(`visualiser levels: ${e}`); });
}

// Everything drawn over the song rather than streamed: a stop dims these with the sound.
const songLayers = [visualiserVideo, visualiserCanvas, visualiserEqCanvas, lyricsCanvas, introLayer];
const background = document.getElementById('background');
const still = document.getElementById('still');

const SCALING = { fit: 'contain', fill: 'cover', stretch: 'fill', original: 'none' };
const placeholder = document.getElementById('placeholder');
const blanked = document.getElementById('blanked');
const hostLost = document.getElementById('hostlost');

// Not `chrome`: a global lexical by that name shadows WebView2's window.chrome, which carries its messaging.
const screenChrome = createScreenChrome({
    doc: document,
    overlay,
    visualiser,
    background,
    loadVisualiserLevels,
    elementAudioSource,
    releaseElementTap,
});

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

// A timer tick, not rAF: rAF stops while the window is occluded (see rampVolume).
const VOLUME_RAMP_TICK_MS = 16;

/// How far an element may have run and still count as at the song's start.
const SONG_START_TOLERANCE_SECONDS = 0.05;

/// The host polls nothing; position reaches it only through these reports.
const STATE_REPORT_MS = 250;

/// A window drag reports every frame; the visualiser is resized once it settles.
const VISUALISER_RESIZE_SETTLE_MS = 150;

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

// An encoded song is listened to through a capture of its element, never by routing the element
// into WebAudio: createMediaElementSource takes the element's sound away from the speakers for
// good, binds it to one context that a sleep can leave dead, and puts the stop fade and the venue
// level behind a second volume control. Where capture is missing (WebKit), the visualiser draws
// without the beat rather than risk the song.
let elementTapContext = null;
let elementTap = null;

function elementAudioSource(el) {
    if (typeof el.captureStream !== 'function') return null;

    const media = el.srcObject || el.currentSrc;
    if (!media || el.readyState < 2) return null;

    if (elementTap && elementTap.el === el && elementTap.media === media) return elementTap.source;

    try {
        const stream = el.captureStream();
        if (stream.getAudioTracks().length === 0) return null;

        elementTapContext = elementTapContext || new (window.AudioContext || window.webkitAudioContext)();
        elementTapContext.resume().catch(() => {});

        releaseElementTap();
        elementTap = { el, media, stream, source: { context: elementTapContext, node: elementTapContext.createMediaStreamSource(stream) } };
        return elementTap.source;
    } catch (e) {
        reportError(`visualiser capture: ${e}`);
        return null;
    }
}

/// Stops a capture: its tracks are copies, so the element plays on.
function releaseElementTap() {
    if (!elementTap) return;

    try { elementTap.source.node.disconnect(); } catch { /* ignore */ }
    for (const track of elementTap.stream.getTracks()) { try { track.stop(); } catch { /* ignore */ } }
    elementTap = null;
}

/// Points the visualiser at whatever the room is hearing now. Asked on every change of that.
function retapVisualiser() {
    // A video is drawn muted and follows nothing it could hear.
    if (!visualiser.active || visualiser.video) return;

    visualiser.setAudio(stemMixer ? stemMixer.analysisSource() : elementAudioSource(current.el));
}

/// Dissolves picture and sound from the outgoing player to the incoming one.
function handOver(arriving) {
    if (incoming !== arriving) return;

    const leaving = current;
    incoming = null;
    current = arriving;
    outgoing = leaving;
    retapVisualiser();

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

        if (!step()) timer = setInterval(step, VOLUME_RAMP_TICK_MS);
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
        }, VOLUME_RAMP_TICK_MS);
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

    // Before the mix's nodes leave the graph under the tap.
    visualiser.setAudio(null);

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
    visualiser.hide();
    visualiser.setAudio(null);
    releaseElementTap();
    reveal(current.el);
    // Cleared before it is shown again, or the frame the fade hid comes back for a moment.
    overlay.clear();
    showWords();
    screenChrome.showIdlePicture();
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

// Debounced: a drag reports every frame.
let visualiserResize = 0;
window.addEventListener('resize', () => {
    clearTimeout(visualiserResize);
    visualiserResize = setTimeout(() => visualiser.resize(), VISUALISER_RESIZE_SETTLE_MS);
});

function onLoad(message) {
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
        retapVisualiser();
        if (message.autoplay === true) {
            visualiser.freeze(false);
            stemMixer.play().catch((e) => reportError(`stem play: ${e}`));
        }
        return;
    }

    detachStems();
    if (message.autoplay === true) visualiser.freeze(false);
    load(message.url, message.autoplay === true, message.pixelated === true);
}

function onVisualiser(message) {
    if (message.enabled === true) {
        visualiser.setLook(message);
        // Tapped once it is up: an imported preset arrives after a fetch, and a tap is
        // asked for only while the visualiser is active.
        visualiser.show(message).then((up) => { if (up) retapVisualiser(); });
        loadVisualiserLevels(message.videoUrl ? null : message.levels);
        if (message.videoUrl) {
            visualiser.setAudio(null);
            releaseElementTap();
        }
        retapVisualiser();
    } else {
        visualiser.hide();
        visualiser.setAudio(null);
        loadVisualiserLevels(null);
        releaseElementTap();
    }
}

function onStemVolume(message) {
    // Silently doing nothing would look exactly like a mix that has stopped responding.
    if (!stemMixer) { reportError('stem-volume with no stems playing'); return; }

    const moved = stemMixer.setStemVolume(message.role, message.voice ?? null, message.volume || 0);
    if (moved === 0) reportError(`stem-volume for ${message.role} ${message.voice ?? ''}, which this song has none of`);
}

function onTimedLyrics(message) {
    // The whole timing document, sent with the load rather than on the transport, and again
    // mid-song when the host changes how it adjusts them. Null clears it, which is what a
    // song with no words looks like. The hold is armed only ahead of a page, since with none
    // there is nothing to lead the singer in to; the play that starts it checks the song is
    // still at its top.
    words = receiveWords(words, message, leadIn);
    showLyrics(words.led);
}

function onPlay() {
    playbackGeneration++;
    showWords();
    placeholder.hidden = true;
    // A fade leaves these mid-ramp. Left alone during a handover: the incoming player is
    // deliberately silent and invisible until it has sound to give.
    if (!incoming) reveal(current.el);
    if (stemMixer) stemMixer.volume = currentVolume;
    visualiser.freeze(false);

    if (leadIn.active) {
        leadIn.resume();
        return;
    }

    if (leadIn.armed && atSongStart()) {
        showLyrics(true);
        leadIn.start();
        reportState();
        return;
    }

    // Once only: a later resume from the top is not a song starting.
    leadIn.cancel();
    target().play().catch((e) => reportError(`play: ${e}`));
}

function onPause() {
    leadIn.pause();
    target().pause();
    visualiser.freeze(true);
}

function onStop(message) {
    // Paused rather than dropped: a play superseding the fade picks it back up, and a
    // fade that completes cancels it with everything else.
    leadIn.pause();
    fadeOutAndStop(Math.max(1, message.fadeMs || 0));
}

function onSeek(message) {
    // The host chose a place in the song, so the rest of any hold goes and the song
    // starts there, running if the hold was.
    const holdWasRunning = leadIn.active && !leadIn.paused;
    leadIn.cancel();
    if (words.led) showLyrics(false);

    // Seeking within a stream the page already holds, rather than restarting an encode.
    try { target().currentTime = message.position || 0; } catch (e) { reportError(`seek: ${e}`); }

    if (holdWasRunning) target().play().catch((e) => reportError(`play: ${e}`));
}

function onHostLost(message) {
    hostLost.hidden = message.lost !== true;
}

function onVideo(message) {
    // Hidden, not paused: a paused element would drift the moment it's turned back on.
    // visibility, not display: display:none drops it from the render tree, stalling WebKit's decoder.
    videos.forEach((v) => { v.style.visibility = message.enabled === false ? 'hidden' : ''; });
    // The words and their card hide the same way, and for the same reason: the engine keeps drawing so
    // the words are still on the song when the picture comes back.
    for (const layer of songLayers) layer.style.visibility = message.enabled === false ? 'hidden' : '';
    blanked.hidden = message.enabled !== false;
}

function onVolume(message) {
    currentVolume = Math.max(0, Math.min(1, message.value));
    // The level rides the whole mix, not one stem: it is the room's volume, and
    // the stems' own levels are what the host set them to against each other.
    if (stemMixer) stemMixer.volume = currentVolume;
    if (!incoming) current.el.volume = currentVolume;
}

function onShowImage(message) {
    // The placeholder is the 'nothing here' card, so it goes while a still is up.
    placeholder.hidden = true;
    // A screen is rarely the same shape as the picture, so the host chooses: contain shows
    // all of it, cover fills and crops, fill distorts, none is native pixels centred.
    still.style.objectFit = SCALING[message.scaling] || 'contain';
    still.src = message.url;
    still.hidden = false;
}

function onHideImage() {
    still.hidden = true;
    still.removeAttribute('src');
    // The card is what an empty screen looks like, so it comes back as the still goes.
    // Every other path that hides the still restores it; leaving it out here meant a venue
    // clearing its picture got a black screen with nothing on it at all, since show-image
    // had already hidden the card and nothing put it back.
    placeholder.hidden = false;
}

function onWindowDragNative() {
    titleBar.nativeDragStarted();
}

function onWindowState(message) {
    titleBar.applyState(message);
}

function onBgLoad(message) {
    loadBackground(message.url, message.autoplay === true);
}

function onBgPlay() {
    backgroundGeneration++;
    background.volume = backgroundVolume;
    background.play().catch((e) => reportError(`bg play: ${e}`));
}

function onBgPause() {
    background.pause();
}

function onBgStop(message) {
    backgroundGeneration++;
    fadeOutBackground(Math.max(0, message.fadeMs || 0));
}

function onBgVolume(message) {
    backgroundVolume = Math.max(0, Math.min(1, message.value));
    background.volume = backgroundVolume;
}

const commandHandlers = {
    'load': onLoad,
    'visualiser': onVisualiser,
    'stem-volume': onStemVolume,
    'timed-lyrics': onTimedLyrics,
    'play': onPlay,
    'pause': onPause,
    'stop': onStop,
    'seek': onSeek,
    'hostLost': onHostLost,
    'video': onVideo,
    'volume': onVolume,
    'show-image': onShowImage,
    'window-drag-native': onWindowDragNative,
    'window-state': onWindowState,
    'hide-image': onHideImage,
    'screen-theme': screenChrome.setScreenTheme,
    'marquee': screenChrome.setMarquee,
    'qr-codes': screenChrome.setQrCodes,
    'break-music-card': screenChrome.setBreakMusicCard,
    'next-singer': screenChrome.showNextSinger,
    'bg-load': onBgLoad,
    'bg-play': onBgPlay,
    'bg-pause': onBgPause,
    'bg-stop': onBgStop,
    'bg-volume': onBgVolume,
};

function handleCommand(raw) {
    let message;
    try { message = JSON.parse(raw); } catch { return; }

    // Taking the screen back: a song starting, or the picture being set. Deliberately not marquee
    // or codes, which would cancel an announcement the host had only just made.
    if (['load', 'play', 'stop', 'show-image', 'hide-image'].includes(message.type)) screenChrome.clearNextSinger();

    // Own keys only: a type such as 'constructor' would otherwise reach Object.prototype.
    if (Object.prototype.hasOwnProperty.call(commandHandlers, message.type)) commandHandlers[message.type](message);
}

// Full screen has no window controls to reach, so the picture itself takes these. The drag strip
// along the top stops its own double-click, which maximises instead.
document.addEventListener('dblclick', () => send({ type: 'toggle-fullscreen' }));
window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') send({ type: 'exit-fullscreen' });
});

videos.forEach((v) => {
    v.addEventListener('loadeddata', () => { placeholder.hidden = true; });
    // A capture has an audio track only once the element is playing something.
    v.addEventListener('playing', () => { if (v === current.el && !stemMixer) retapVisualiser(); });
    // Only from the player the room is hearing: the outgoing one runs out during a handover, and
    // that would retire the singer on the strength of a stream nobody is listening to any more.
    v.addEventListener('ended', () => {
        if (v === current.el) send({ type: 'ended', position: v.currentTime, sampledAtEpochMs: Date.now() });
    });
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
        retapVisualiser();
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

// A stem mix has no element to fire 'ended', so it is told once per run to its end: the host
// otherwise waits on a clock that a mix sitting past its last sample never corrects.
let stemEndReported = null;

function reportStemEnd() {
    if (!stemMixer) return;

    if (!stemMixer.ended) {
        if (stemEndReported === stemMixer) stemEndReported = null;
        return;
    }

    if (stemEndReported === stemMixer) return;
    stemEndReported = stemMixer;
    send({ type: 'ended', position: stemMixer.currentTime, sampledAtEpochMs: Date.now() });
}

// The host polls nothing; position reaches it only through these reports.
setInterval(() => {
    watchForWake();
    reportStemEnd();
    reportState();
}, STATE_REPORT_MS);

function reportState() {
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
        // Said outright rather than left to the zero: the host freezes its clock on it.
        holding,
        // Sample time, not send time: guessed latency would bias the host's playhead forever.
        sampledAtEpochMs: Date.now(),
        rate: player.playbackRate,
        readyState: player.readyState,
        // A stem mix's context state; undefined for an element, so it drops out of the JSON.
        audioState: player.audioState,
    });
}

if (window.external && window.external.receiveMessage) {
    window.external.receiveMessage(handleCommand);
}

// Whether this engine decodes only royalty-free formats: stock Chromium (Oriel without its codec
// runtime) has no H.264 or AAC, so the host must send VP9 and Opus instead.
function decodesOnlyOpenCodecs() {
    const probe = window.MediaSource && MediaSource.isTypeSupported;
    return probe ? !MediaSource.isTypeSupported('video/mp4; codecs="avc1.4d401f,mp4a.40.2"') : false;
}

// Last line on purpose: until this arrives the host doesn't know the screen exists, and a command
// pushed into a page-less web view crashes the whole process inside Photino's SendWebMessage.
send({ type: 'ready', openCodecsOnly: decodesOnlyOpenCodecs() });
