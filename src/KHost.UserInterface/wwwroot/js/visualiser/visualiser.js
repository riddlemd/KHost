// A MilkDrop preset (butterchurn, WebGL 2) drawn under the words of a song that has nothing of its
// own to show there. The host decides whether and which; this only draws.
//
// One engine for the page's life, built with no audio context of its own: butterchurn binds the
// context it is built with, and every stem song brings a fresh one. The samples are read here from
// whichever tap the song offers and handed to each frame instead. A song with no tap (an encoded
// one in WebKit, which has no captureStream) is drawn from the levels the host read from its
// source, rebuilt into samples at the song's own clock.

/// Frames a second. The song comes first: every frame is GPU and main-thread time taken from the
/// decoder, the mixer and the words, and a visualiser at 30 reads as smooth.
const VISUALISER_FPS = 30;

/// The drawing buffer's ceiling, scaled up to the window by CSS. A MilkDrop picture is soft by
/// nature, and the cost is per pixel.
const VISUALISER_MAX_WIDTH = 1280;
const VISUALISER_MAX_HEIGHT = 720;

/// The sample count butterchurn analyses per frame, and so what each analyser must be sized to.
const VISUALISER_SAMPLES = 1024;

/// The host's bands, as SongLevels.cs splits them, in Hz; change one and change the other. 320 and
/// 2800 are butterchurn's own bass/mid/treble splits.
const VISUALISER_BAND_EDGES = [20, 50, 125, 320, 700, 1400, 2800, 5600, 11025];

/// The rate butterchurn takes handed samples to be at when it was built with no audio context.
const VISUALISER_SAMPLE_RATE = 44100;

/// One band's tone at its loudest, on the byte scale's ±128. Several at full clip, which only
/// roughens what is already the loudest moment.
const VISUALISER_TONE_PEAK = 40;

/// A levels track as the host serves it (see SongLevels.cs for the layout), or null.
function parseVisualiserLevels(buffer) {
    const bytes = new Uint8Array(buffer || new ArrayBuffer(0));
    if (bytes.length < 12 || String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]) !== 'KHLV' || bytes[4] !== 1) return null;

    const fps = bytes[5], bands = bytes[6], channels = bytes[7];
    const frames = new DataView(bytes.buffer, bytes.byteOffset, 12).getUint32(8, true);

    if (!fps || !channels || bands !== VISUALISER_BAND_EDGES.length - 1) return null;
    if (bytes.length !== 12 + frames * bands * channels) return null;

    return { fps, bands, channels, frames, data: bytes.subarray(12) };
}

/// The track's frame a song position falls in; -1 before the song (a lead-in's hold) or past it.
function visualiserLevelsFrame(track, songSeconds) {
    // A typeof check, not `>= 0` alone: null compares as 0, which reads a song nobody is holding.
    if (!track || typeof songSeconds !== 'number' || !(songSeconds >= 0)) return -1;

    const frame = Math.floor(songSeconds * track.fps);
    return frame < track.frames ? frame : -1;
}

const visualiserBandCentres = VISUALISER_BAND_EDGES.slice(1).map((top, i) => Math.sqrt(top * VISUALISER_BAND_EDGES[i]));
let visualiserScratch = null;

/// Fills butterchurn's three arrays with one tone per band at that band's level for this song
/// position: its own FFT reads them back as the song's bass, mid and treble. Phases follow the song
/// clock, so the waveform a preset draws moves with the song rather than standing still. Returns
/// false, leaving silence, where the track has nothing.
function synthesiseVisualiserLevels(track, songSeconds, levels) {
    const frame = visualiserLevelsFrame(track, songSeconds);
    const outputs = [levels.timeByteArrayL, levels.timeByteArrayR];

    if (frame < 0) {
        levels.timeByteArray.fill(128);
        for (const out of outputs) out.fill(128);
        return false;
    }

    const length = levels.timeByteArray.length;
    if (!visualiserScratch || visualiserScratch[0].length !== length) {
        visualiserScratch = [new Float32Array(length), new Float32Array(length)];
    }

    const base = frame * track.channels * track.bands;

    for (let channel = 0; channel < 2; channel++) {
        const sum = visualiserScratch[channel];
        const row = base + Math.min(channel, track.channels - 1) * track.bands;
        sum.fill(0);

        for (let band = 0; band < track.bands; band++) {
            const level = track.data[row + band];
            if (level === 0) continue;

            // A byte is a quarter decibel under the band's loudest at 255.
            const amplitude = VISUALISER_TONE_PEAK * Math.pow(10, (level - 255) / 80);
            const hz = visualiserBandCentres[band];
            const step = 2 * Math.PI * hz / VISUALISER_SAMPLE_RATE;
            const start = 2 * Math.PI * ((hz * songSeconds) % 1);

            // sin(a + (k+1)s) = 2cos(s)·sin(a + ks) − sin(a + (k−1)s): one multiply a sample.
            const twice = 2 * Math.cos(step);
            let previous = Math.sin(start - step);
            let value = Math.sin(start);

            for (let k = 0; k < length; k++) {
                sum[k] += amplitude * value;
                const next = twice * value - previous;
                previous = value;
                value = next;
            }
        }
    }

    const clip = (v) => Math.max(0, Math.min(255, Math.round(128 + v)));
    for (let k = 0; k < length; k++) {
        const left = visualiserScratch[0][k];
        const right = visualiserScratch[1][k];
        levels.timeByteArrayL[k] = clip(left);
        levels.timeByteArrayR[k] = clip(right);
        levels.timeByteArray[k] = clip((left + right) / 2);
    }

    return true;
}

/// A shipped preset by the name the host sends, or null. Exact: the host holds the same list.
function findVisualiserPreset(presets, name) {
    const found = typeof name === 'string' ? (presets || []).find((p) => p.name === name) : null;
    return found ? found.preset : null;
}

/// The CSS filter for a look's brightness and colour, in percent; empty when both are as made.
/// A filter on the canvas rather than a pass in the engine: the compositor applies it for free.
function visualiserFilter(brightness, saturation) {
    const b = Number.isFinite(brightness) ? brightness : 100;
    const s = Number.isFinite(saturation) ? saturation : 100;
    return b === 100 && s === 100 ? '' : `brightness(${b / 100}) saturate(${s / 100})`;
}

/// How far a frame's loudness may be pushed either way: past this the picture only clips or stalls.
const VISUALISER_MAX_SWING = 8;

/// Scales how far each frame's loudness strays from its own recent average: 1 leaves the sound as
/// heard, 2 doubles every swing in decibels, 0 flattens it to its average. Butterchurn reads each
/// band against its own running average, so a plain gain would cancel out; the swing is what it
/// reacts to. Applied to whatever filled the samples, a live tap or the host's levels alike.
/// `state` carries the running average between frames. Returns the gain applied.
function applyVisualiserSensitivity(levels, sensitivity, state) {
    const all = levels.timeByteArray;
    let sum = 0;
    for (let k = 0; k < all.length; k++) { const d = all[k] - 128; sum += d * d; }
    const rms = Math.sqrt(sum / (all.length || 1));

    // Tracked whatever the setting, so a change mid-song starts from where the song is.
    state.average = state.average === undefined || state.average === null ? rms : state.average * 0.95 + rms * 0.05;

    // Silence has no swing to scale, and an average of nothing would divide by zero.
    if (sensitivity === 1 || !(rms >= 0.5) || !(state.average >= 0.5)) return 1;

    const gain = Math.min(VISUALISER_MAX_SWING, Math.max(1 / VISUALISER_MAX_SWING, Math.pow(rms / state.average, sensitivity - 1)));
    for (const array of [levels.timeByteArray, levels.timeByteArrayL, levels.timeByteArrayR]) {
        for (let k = 0; k < array.length; k++) {
            array[k] = Math.max(0, Math.min(255, Math.round(128 + (array[k] - 128) * gain)));
        }
    }

    return gain;
}

/// The drawing buffer for a canvas shown at this size: its shape, no bigger than the ceiling.
function visualiserBufferSize(width, height) {
    const w = Math.max(1, Math.round(width || 0));
    const h = Math.max(1, Math.round(height || 0));
    const scale = Math.min(1, VISUALISER_MAX_WIDTH / w, VISUALISER_MAX_HEIGHT / h);

    return { width: Math.max(1, Math.round(w * scale)), height: Math.max(1, Math.round(h * scale)) };
}

/// Listens to one node without changing where its sound goes: a fan-out to analysers that lead
/// nowhere, so what reaches the speakers is untouched.
function createVisualiserTap(context, node) {
    const analyser = () => {
        const a = context.createAnalyser();
        a.fftSize = VISUALISER_SAMPLES;
        a.smoothingTimeConstant = 0;
        return a;
    };

    const both = analyser();
    const left = analyser();
    const right = analyser();
    const splitter = context.createChannelSplitter(2);

    node.connect(both);
    node.connect(splitter);
    splitter.connect(left, 0);
    splitter.connect(right, 1);

    return {
        context,
        node,
        read(levels) {
            both.getByteTimeDomainData(levels.timeByteArray);
            left.getByteTimeDomainData(levels.timeByteArrayL);
            right.getByteTimeDomainData(levels.timeByteArrayR);
        },
        disconnect() {
            // Only this tap's own branches: the node's route to the speakers stays.
            try { node.disconnect(both); } catch { /* context already closed */ }
            try { node.disconnect(splitter); } catch { /* context already closed */ }
        },
    };
}

/// `engine` is butterchurn and `presets` the curated list; both are passed in so the logic here
/// can be exercised without WebGL.
/// `clock` answers the song position in seconds (null when nothing holds the song), for the host
/// levels to be read at. `fetchPreset` answers an imported preset's URL with its parsed file.
function createVisualiser(canvas, { engine, presets, reportError, frame, cancelFrame, now, clock: songClock, fetchPreset }) {
    const requestFrame = frame || ((cb) => window.requestAnimationFrame(cb));
    const cancel = cancelFrame || ((id) => window.cancelAnimationFrame(id));
    const clock = now || (() => performance.now());
    const fetchJson = fetchPreset || ((url) => fetch(url).then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`)))));

    let viz = null;
    let broken = false;
    // What is drawn (the preset's name or URL) and what was last asked for, which a slow fetch
    // must not overtake.
    let shownKey = null;
    let wantedKey = null;
    let sensitivity = 1;
    const swing = {};
    let frozen = false;
    let handle = null;
    let lastFrameAt = -Infinity;
    let tap = null;
    let track = null;

    // Silence is 128 on this scale, so a song with no tap still draws, just without the beat.
    const levels = {
        timeByteArray: new Uint8Array(VISUALISER_SAMPLES).fill(128),
        timeByteArrayL: new Uint8Array(VISUALISER_SAMPLES).fill(128),
        timeByteArrayR: new Uint8Array(VISUALISER_SAMPLES).fill(128),
    };

    function silence() {
        levels.timeByteArray.fill(128);
        levels.timeByteArrayL.fill(128);
        levels.timeByteArrayR.fill(128);
    }

    function sizeBuffer() {
        const size = visualiserBufferSize(canvas.clientWidth || window.innerWidth, canvas.clientHeight || window.innerHeight);
        if (canvas.width === size.width && canvas.height === size.height) return null;

        canvas.width = size.width;
        canvas.height = size.height;
        return size;
    }

    /// Built on first use, never at page load: a venue that never turns it on pays nothing.
    function ensureEngine() {
        if (viz || broken) return viz;

        try {
            const size = sizeBuffer() || { width: canvas.width, height: canvas.height };
            viz = engine.createVisualizer(null, canvas, { width: size.width, height: size.height, pixelRatio: 1, textureRatio: 1 });
        } catch (e) {
            // No WebGL 2 here: the song plays over black, as it would with the setting off.
            broken = true;
            reportError(`visualiser: ${e}`);
        }

        return viz;
    }

    function running() {
        return viz !== null && shownKey !== null && !frozen;
    }

    function schedule() {
        if (handle === null && running()) handle = requestFrame(tick);
    }

    function unschedule() {
        if (handle === null) return;
        cancel(handle);
        handle = null;
    }

    // rAF rather than a timer: it stops while the window is hidden, which is the pause wanted here.
    function tick() {
        handle = null;
        if (!running()) return;

        schedule();

        // A small allowance so a 60Hz display lands on every second frame rather than drifting.
        const at = clock();
        if (at - lastFrameAt < 1000 / VISUALISER_FPS - 4) return;
        lastFrameAt = at;

        try {
            if (tap) tap.read(levels);
            else if (track) synthesiseVisualiserLevels(track, songClock ? songClock() : null, levels);
            applyVisualiserSensitivity(levels, sensitivity, swing);
            viz.render({ audioLevels: levels });
        } catch (e) {
            reportError(`visualiser frame: ${e}`);
            hide();
        }
    }

    function hide() {
        shownKey = null;
        wantedKey = null;
        unschedule();
        canvas.hidden = true;
    }

    function showPreset(key, preset) {
        if (key !== shownKey) {
            try { viz.loadPreset(preset, 0); } catch (e) { reportError(`visualiser preset: ${e}`); hide(); return false; }
        }

        shownKey = key;
        canvas.hidden = false;
        schedule();
        return true;
    }

    const api = {
        /// Shows a shipped preset by `presetName` or an imported one from `presetUrl`, answering
        /// whether it is up. The same one again leaves the picture running, so a rebuild at a new
        /// key does not restart it. One that cannot be found or read leaves black.
        show({ presetName, presetUrl } = {}) {
            const key = presetUrl || presetName || null;
            wantedKey = key;
            if (!key || !ensureEngine()) { hide(); return Promise.resolve(false); }

            if (key === shownKey) return Promise.resolve(showPreset(key, null));

            if (!presetUrl) {
                const preset = findVisualiserPreset(presets, presetName);
                if (!preset) { reportError(`visualiser preset: none called ${presetName}`); hide(); return Promise.resolve(false); }
                return Promise.resolve(showPreset(key, preset));
            }

            return Promise.resolve()
                .then(() => fetchJson(presetUrl))
                .then((preset) => (wantedKey === key ? showPreset(key, preset) : false))
                .catch((e) => {
                    if (wantedKey === key) { reportError(`visualiser preset ${presetUrl}: ${e}`); hide(); }
                    return false;
                });
        },

        /// How it is drawn: brightness and colour in percent, sensitivity in percent of the swing.
        setLook({ brightness, saturation, sensitivity: react } = {}) {
            canvas.style.filter = visualiserFilter(brightness, saturation);
            sensitivity = Number.isFinite(react) ? Math.max(0, react) / 100 : 1;
        },

        hide,

        /// Paused, the last frame holds: motion under a stopped song reads as the song still going.
        freeze(value) {
            frozen = value === true;
            if (frozen) unschedule(); else schedule();
        },

        /// Listens to `source` ({ context, node }), or to nothing. The same node twice keeps the tap.
        setAudio(source) {
            if (tap && source && tap.context === source.context && tap.node === source.node) return;

            if (tap) tap.disconnect();
            tap = null;
            silence();

            if (!source) return;

            try { tap = createVisualiserTap(source.context, source.node); } catch (e) { reportError(`visualiser tap: ${e}`); }
        },

        /// The host's levels for the song (parseVisualiserLevels), or null. Drawn from only while no
        /// tap is listening: a tap hears the room itself.
        setLevels(levelsTrack) {
            track = levelsTrack || null;
            if (!tap) silence();
        },

        /// Re-sizes the drawing buffer to the window; a no-op until something is drawn.
        resize() {
            if (!viz) return;

            const size = sizeBuffer();
            if (size) viz.setRendererSize(size.width, size.height, { pixelRatio: 1, textureRatio: 1 });
        },

        get active() { return shownKey !== null; },
        get running() { return running(); },
        get presetName() { return shownKey; },
        get sensitivity() { return sensitivity; },
        get listening() { return tap !== null; },
        get hostLevels() { return track !== null; },
    };

    return api;
}
