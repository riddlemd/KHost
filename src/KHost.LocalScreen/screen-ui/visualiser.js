// A MilkDrop preset (butterchurn, WebGL 2) drawn under the words of a song that has nothing of its
// own to show there. The host decides whether and which; this only draws.
//
// One engine for the page's life, built with no audio context of its own: butterchurn binds the
// context it is built with, and every stem song brings a fresh one. The samples are read here from
// whichever tap the song offers and handed to each frame instead.

/// Frames a second. The song comes first: every frame is GPU and main-thread time taken from the
/// decoder, the mixer and the words, and a visualiser at 30 reads as smooth.
const VISUALISER_FPS = 30;

/// The drawing buffer's ceiling, scaled up to the window by CSS. A MilkDrop picture is soft by
/// nature, and the cost is per pixel.
const VISUALISER_MAX_WIDTH = 1280;
const VISUALISER_MAX_HEIGHT = 720;

/// The sample count butterchurn analyses per frame, and so what each analyser must be sized to.
const VISUALISER_SAMPLES = 1024;

/// Which preset a number names. Any integer, negative included, lands inside the set.
function visualiserPresetIndex(number, count) {
    if (!(count > 0)) return -1;
    const n = Math.trunc(Number(number) || 0);
    return ((n % count) + count) % count;
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
function createVisualiser(canvas, { engine, presets, reportError, frame, cancelFrame, now }) {
    const requestFrame = frame || ((cb) => window.requestAnimationFrame(cb));
    const cancel = cancelFrame || ((id) => window.cancelAnimationFrame(id));
    const clock = now || (() => performance.now());

    let viz = null;
    let broken = false;
    let presetIndex = -1;
    let frozen = false;
    let handle = null;
    let lastFrameAt = -Infinity;
    let tap = null;

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
        return viz !== null && presetIndex >= 0 && !frozen;
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
            viz.render({ audioLevels: levels });
        } catch (e) {
            reportError(`visualiser frame: ${e}`);
            hide();
        }
    }

    function hide() {
        presetIndex = -1;
        unschedule();
        canvas.hidden = true;
    }

    const api = {
        /// Shows the preset a number names. The same number again leaves the picture running, so a
        /// rebuild at a new key does not restart it.
        show(number) {
            const index = visualiserPresetIndex(number, presets.length);
            if (index < 0 || !ensureEngine()) { hide(); return; }

            if (index !== presetIndex) {
                try { viz.loadPreset(presets[index].preset, 0); } catch (e) { reportError(`visualiser preset: ${e}`); hide(); return; }
            }

            presetIndex = index;
            canvas.hidden = false;
            schedule();
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

        /// Re-sizes the drawing buffer to the window; a no-op until something is drawn.
        resize() {
            if (!viz) return;

            const size = sizeBuffer();
            if (size) viz.setRendererSize(size.width, size.height, { pixelRatio: 1, textureRatio: 1 });
        },

        get active() { return presetIndex >= 0; },
        get running() { return running(); },
        get presetName() { return presetIndex >= 0 ? presets[presetIndex].name : null; },
        get listening() { return tap !== null; },
    };

    return api;
}
