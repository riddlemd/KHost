// The host's own drawings under the words: a spectrum analyser, the same mirrored, an
// oscilloscope and a pair of VU meters, a set of calm ambient scenes and a set of old-screen (retro)
// effects. Canvas 2D, no third-party code. visualiser.js decides when one is up and hands each frame
// what it heard; this only turns that into a picture.
//
// The analysers sit low on the screen, under where the words usually are, so the picture behind a
// line stays dark. The ambient and retro scenes fill the screen but stay dim, and the music only
// nudges them. Frame-counted motion (peak holds, falls, every scene's movement) assumes
// visualiser.js's 30fps cap, and is why a frozen picture resumes where it stopped.

/// The styles a playlist entry can name, by the name the host sends. VisualiserPresetService
/// mirrors this list; a test holds them together. A name starting 'ambient-' is a calm scene and
/// one starting 'retro-' an old-screen effect; the Visualisations page groups by those prefixes.
const EQ_VISUALISER_STYLES = [
    { name: 'spectrum-bars', title: 'Spectrum bars' },
    { name: 'mirrored-bars', title: 'Mirrored bars' },
    { name: 'oscilloscope', title: 'Oscilloscope' },
    { name: 'vu-meters', title: 'Twin VU meters' },
    { name: 'ambient-gradient', title: 'Drifting colour' },
    { name: 'ambient-bokeh', title: 'Floating lights' },
    { name: 'ambient-embers', title: 'Rising embers' },
    { name: 'ambient-rings', title: 'Pulse rings' },
    { name: 'ambient-beams', title: 'Sweeping beams' },
    { name: 'retro-static', title: 'TV static' },
    { name: 'retro-vhs', title: 'VHS tracking' },
    { name: 'retro-crt', title: 'CRT glow' },
    { name: 'retro-glitch', title: 'Glitch blocks' },
    { name: 'retro-bars', title: 'Rolling colour bars' },
];

/// Retro scenes: the grain is drawn at this size and scaled up, never generated per screen pixel,
/// and only RETRO_GRAIN_TILES frames of it are made, once, then cycled.
const RETRO_GRAIN_WIDTH = 320;
const RETRO_GRAIN_HEIGHT = 180;
const RETRO_GRAIN_TILES = 6;

/// A glitch holds RETRO_GLITCH_HOLD frames; a beat may start one at most every RETRO_GLITCH_GAP,
/// and a passage with no beat gets one every RETRO_GLITCH_IDLE.
const RETRO_GLITCH_GAP = 24;
const RETRO_GLITCH_IDLE = 150;
const RETRO_GLITCH_HOLD = 6;

/// Classic, for a retro scene: each effect's own look rather than a meter's green to red.
const RETRO_CLASSIC = {
    'retro-static': { main: ['#c8ccd6', '#9aa3b5'], grain: '#c8ccd6' },
    'retro-vhs': { main: ['#1f3c9c', '#3a2a7a'], grain: '#c8ccd6', red: '#ff2850', cyan: '#28dcff' },
    'retro-crt': { main: ['#33ff99', '#ffb347', '#4da6ff', '#c77dff'], grain: '#c8ccd6' },
    'retro-glitch': { main: ['#ff2e88', '#22e0ff', '#7b5cff', '#1fd17a'], grain: '#c8ccd6', red: '#ff2850', cyan: '#28dcff' },
    'retro-bars': { main: ['#c0c0c0', '#c0c000', '#00c0c0', '#00c000', '#c000c0', '#c00000', '#0000c0'], grain: '#c8ccd6' },
};

/// The most any one ambient shape is painted at. Shapes lay over black, so no pixel is ever brighter
/// than this share of its colour from one shape, which keeps the loudest moment a glow.
const AMBIENT_MAX_ALPHA = 0.4;

/// How far the smoothed loudness may move in one frame: a hit swells over several frames and dies
/// away over a couple of seconds, so a scene can brighten with the music but never flash.
const AMBIENT_RISE = 0.04;
const AMBIENT_FALL = 0.012;

/// A beat is the bass jumping this far clear of its own recent average, at most one per gap.
const AMBIENT_BEAT_JUMP = 0.12;
const AMBIENT_BEAT_GAP = 12;

/// Pulse rings: how long one lives, and the longest wait for one in a passage with no beat.
const AMBIENT_RING_LIFE = 90;
const AMBIENT_RING_IDLE = 75;

/// The seed a scene's layout starts from unless the caller names one, so a still is repeatable.
const AMBIENT_SEED = 0x4b486f73;

/// Classic, for a scene: a soft spread of colours rather than a meter's green to red.
const AMBIENT_CLASSIC = ['#2ec4b6', '#6c63ff', '#d6589b', '#f0a04b', '#3a86ff'];

/// The bar counts an entry may ask for; anything else is taken as the nearest.
const EQ_BAR_COUNTS = [16, 32, 64];
const EQ_DEFAULT_BAR_COUNT = 32;

/// The span the bars cover, in Hz: the host's band edges end to end, so the two feeds agree.
const EQ_LOW_HZ = 20;
const EQ_HIGH_HZ = 11025;

/// How far under a band's own loudest a host-levels bar reaches the floor. The host keeps each
/// band against its own loudest, so a bar at the top is that band at its loudest in the song.
const EQ_BAND_RANGE_DB = 36;

/// A live tap's spectrum, in dB of the analyser's own scale: the floor and the top of a bar.
const EQ_FFT_FLOOR_DB = -85;
const EQ_FFT_TOP_DB = -30;

/// Bars fall at most this much of their height a frame; a peak cap holds, then falls slower.
const EQ_BAR_FALL = 0.05;
const EQ_PEAK_HOLD_FRAMES = 15;
const EQ_PEAK_FALL = 0.012;

/// VU meters: the dB span shown below full scale, and the peak hold.
const EQ_VU_RANGE_DB = 48;
const EQ_VU_FALL = 0.06;
const EQ_VU_PEAK_HOLD_FRAMES = 30;
const EQ_VU_PEAK_FALL = 0.01;
const EQ_VU_SEGMENTS = 40;

/// The screen's own accent: the colour the words are sung in when a timing sets none.
const EQ_THEME_COLOUR = '#8558fa';
const EQ_SINGLE_COLOUR = '#33ccff';

/// Whether a style of that name exists.
function isEqVisualiserStyle(name) {
    return EQ_VISUALISER_STYLES.some((s) => s.name === name);
}

/// The allowed bar count nearest to what was asked; the default for anything not a number.
function eqBarCount(asked) {
    if (!Number.isFinite(asked)) return EQ_DEFAULT_BAR_COUNT;
    return EQ_BAR_COUNTS.reduce((best, n) => (Math.abs(n - asked) < Math.abs(best - asked) ? n : best), EQ_BAR_COUNTS[0]);
}

/// `count` bars' edges in Hz, spaced evenly on a log scale from EQ_LOW_HZ to EQ_HIGH_HZ.
function eqBarEdges(count) {
    const edges = new Array(count + 1);
    const ratio = Math.log(EQ_HIGH_HZ / EQ_LOW_HZ);
    for (let i = 0; i <= count; i++) edges[i] = EQ_LOW_HZ * Math.exp((ratio * i) / count);
    return edges;
}

const eqClamp01 = (v) => (v > 1 ? 1 : v > 0 ? v : 0);

/// A host levels byte (a quarter decibel under the band's loudest at 255; 0 is silence) as a bar
/// height from 0 to 1.
function eqHeightFromBandByte(byte) {
    if (!(byte > 0)) return 0;
    return eqClamp01(((byte - 255) / 4 + EQ_BAND_RANGE_DB) / EQ_BAND_RANGE_DB);
}

/// Bar heights from a handful of band heights (`bandHeights[i]` for the band between
/// `bandEdges[i]` and `bandEdges[i + 1]`): each bar reads the bands at its own centre frequency,
/// straight-line between neighbouring band centres on a log scale, and held flat past the ends.
/// With eight bands, bars between two centres ramp from one to the other; they cannot move apart.
function eqBarsFromBands(bandHeights, bandEdges, count, out) {
    const heights = out || new Float32Array(count);
    const edges = eqBarEdges(count);
    const centres = bandHeights.map((_, i) => Math.log(Math.sqrt(bandEdges[i] * bandEdges[i + 1])));
    const last = centres.length - 1;

    for (let bar = 0; bar < count; bar++) {
        const at = Math.log(Math.sqrt(edges[bar] * edges[bar + 1]));
        let value;
        if (at <= centres[0]) value = bandHeights[0];
        else if (at >= centres[last]) value = bandHeights[last];
        else {
            let i = 0;
            while (at > centres[i + 1]) i++;
            const t = (at - centres[i]) / (centres[i + 1] - centres[i]);
            value = bandHeights[i] + (bandHeights[i + 1] - bandHeights[i]) * t;
        }
        heights[bar] = eqClamp01(value);
    }

    return heights;
}

/// Bar heights from an analyser's byte spectrum (`bytes[k]` for `k * binHz`, on a scale from
/// `minDb` to `maxDb`): each bar takes its loudest bin, and a bar narrower than a bin (the low end
/// of a short FFT) reads between the two bins either side of its centre.
function eqBarsFromSpectrum(bytes, binHz, minDb, maxDb, count, out) {
    const heights = out || new Float32Array(count);
    const edges = eqBarEdges(count);
    const span = maxDb - minDb;
    const toHeight = (byte) => eqClamp01((minDb + (byte / 255) * span - EQ_FFT_FLOOR_DB) / (EQ_FFT_TOP_DB - EQ_FFT_FLOOR_DB));

    for (let bar = 0; bar < count; bar++) {
        const from = Math.ceil(edges[bar] / binHz);
        const to = Math.min(bytes.length - 1, Math.ceil(edges[bar + 1] / binHz) - 1);
        let loudest = -1;
        for (let k = from; k <= to; k++) if (bytes[k] > loudest) loudest = bytes[k];

        if (loudest < 0) {
            const x = Math.sqrt(edges[bar] * edges[bar + 1]) / binHz;
            const k = Math.min(bytes.length - 2, Math.floor(x));
            const t = x - k;
            loudest = bytes[k] + (bytes[k + 1] - bytes[k]) * t;
        }

        heights[bar] = toHeight(loudest);
    }

    return heights;
}

/// Moves shown bars and their peak caps one frame on: a bar jumps up and falls at most
/// EQ_BAR_FALL; its cap rides it up, holds EQ_PEAK_HOLD_FRAMES, then falls EQ_PEAK_FALL a frame and
/// never below the bar. `state` is { bars, peaks, holds }, sized on first use.
function eqStepBars(state, heights) {
    const n = heights.length;
    if (!state.bars || state.bars.length !== n) {
        state.bars = new Float32Array(n);
        state.peaks = new Float32Array(n);
        state.holds = new Int16Array(n);
    }

    for (let i = 0; i < n; i++) {
        const shown = Math.max(heights[i], state.bars[i] - EQ_BAR_FALL, 0);
        state.bars[i] = shown;

        if (shown >= state.peaks[i]) {
            state.peaks[i] = shown;
            state.holds[i] = EQ_PEAK_HOLD_FRAMES;
        } else if (state.holds[i] > 0) {
            state.holds[i]--;
        } else {
            state.peaks[i] = Math.max(shown, state.peaks[i] - EQ_PEAK_FALL);
        }
    }

    return state;
}

/// A channel's level as a meter reading from 0 to 1: the RMS of byte samples (128 is silence),
/// EQ_VU_RANGE_DB under full scale at 0.
function eqVuHeight(samples) {
    let sum = 0;
    for (let k = 0; k < samples.length; k++) { const d = (samples[k] - 128) / 128; sum += d * d; }
    const rms = Math.sqrt(sum / (samples.length || 1));
    if (!(rms > 0)) return 0;
    return eqClamp01((20 * Math.log10(rms) + EQ_VU_RANGE_DB) / EQ_VU_RANGE_DB);
}

/// Moves the two meters one frame on, as eqStepBars does for bars but with a meter's slower hold.
/// `state` is { levels, peaks, holds }.
function eqStepVu(state, left, right) {
    if (!state.levels) {
        state.levels = [0, 0];
        state.peaks = [0, 0];
        state.holds = [0, 0];
    }

    [left, right].forEach((reading, i) => {
        const shown = Math.max(reading, state.levels[i] - EQ_VU_FALL, 0);
        state.levels[i] = shown;

        if (shown >= state.peaks[i]) {
            state.peaks[i] = shown;
            state.holds[i] = EQ_VU_PEAK_HOLD_FRAMES;
        } else if (state.holds[i] > 0) {
            state.holds[i]--;
        } else {
            state.peaks[i] = Math.max(shown, state.peaks[i] - EQ_VU_PEAK_FALL);
        }
    });

    return state;
}

/// '#rrggbb' as [r, g, b], or null for anything else.
function eqParseColour(value) {
    const m = typeof value === 'string' ? /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(value.trim()) : null;
    return m ? [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)] : null;
}

/// The colour stops along a meter, from its floor (0) to full (1): Winamp's green, yellow, red;
/// the accent from dark to light; or one colour throughout.
function eqColourStops(scheme, colour) {
    if (scheme === 'theme' || scheme === 'single') {
        const rgb = eqParseColour(scheme === 'theme' ? EQ_THEME_COLOUR : colour) || eqParseColour(scheme === 'theme' ? EQ_THEME_COLOUR : EQ_SINGLE_COLOUR);
        const css = (f) => `rgb(${Math.round(rgb[0] * f)},${Math.round(rgb[1] * f)},${Math.round(rgb[2] * f)})`;
        return scheme === 'theme'
            ? [[0, css(0.45)], [1, `rgb(${rgb.map((c) => Math.round(c + (255 - c) * 0.35)).join(',')})`]]
            : [[0, css(1)], [1, css(1)]];
    }

    return [[0, '#00b400'], [0.55, '#28dc00'], [0.75, '#f0e000'], [0.9, '#ff7800'], [1, '#ff1e00']];
}

/// Whether a style is one of the calm scenes rather than an analyser.
function isAmbientStyle(name) {
    return typeof name === 'string' && name.startsWith('ambient-') && isEqVisualiserStyle(name);
}

/// The colours a scene draws with, as [r, g, b]: the classic spread, or the accent or the single
/// colour with a lighter and a darker shade of it, so a one-colour scene still has depth.
function ambientPalette(scheme, colour) {
    if (scheme === 'theme' || scheme === 'single') {
        const rgb = eqParseColour(scheme === 'theme' ? EQ_THEME_COLOUR : colour) || eqParseColour(EQ_SINGLE_COLOUR);
        const shade = (f) => rgb.map((c) => Math.round(f >= 0 ? c + (255 - c) * f : c * (1 + f)));
        return [rgb, shade(0.3), shade(-0.35)];
    }

    return AMBIENT_CLASSIC.map(eqParseColour);
}

/// A colour `at` of the way round the palette (wrapping), blended between neighbours so a scene's
/// colours drift rather than jump.
function ambientColourAt(palette, at) {
    const n = palette.length;
    const x = ((at % n) + n) % n;
    const i = Math.floor(x), t = x - i;
    const a = palette[i], b = palette[(i + 1) % n];
    return [0, 1, 2].map((k) => Math.round(a[k] + (b[k] - a[k]) * t));
}

/// Whether a style is one of the old-screen effects. They follow the ambient scenes' rules: the same
/// eased loudness, the same AMBIENT_MAX_ALPHA ceiling on anything that adds light.
function isRetroStyle(name) {
    return typeof name === 'string' && name.startsWith('retro-') && isEqVisualiserStyle(name);
}

/// A retro scene's colours as [r, g, b]: `main` (its picture), `grain` (the snow), and `red` and
/// `cyan` (a chroma fringe). Classic is the effect's own look; the accent or a single colour turns
/// every one of them into a shade of that colour, the fringes included.
function retroColours(style, scheme, colour) {
    if (scheme === 'theme' || scheme === 'single') {
        const [base, light, dark] = ambientPalette(scheme, colour);
        return { main: [base, light, dark], grain: light, red: light, cyan: dark };
    }

    const look = RETRO_CLASSIC[style] || RETRO_CLASSIC['retro-static'];
    const grain = eqParseColour(look.grain);
    return {
        main: look.main.map(eqParseColour),
        grain,
        red: eqParseColour(look.red || look.grain),
        cyan: eqParseColour(look.cyan || look.grain),
    };
}

/// A canvas off the page to hold the grain, or null where there is none to make.
function retroCanvas(width, height) {
    let made = null;
    if (typeof document !== 'undefined' && document.createElement) made = document.createElement('canvas');
    else if (typeof OffscreenCanvas === 'function') made = new OffscreenCanvas(width, height);
    if (made) { made.width = width; made.height = height; }
    return made;
}

/// Fills `image` (an ImageData) with one frame of snow in `rgb`: mostly dark specks, a few bright,
/// so the grain reads as snow rather than a grey haze.
function retroFillGrain(image, rgb, random) {
    const data = image.data;
    for (let i = 0; i < data.length; i += 4) {
        const v = random();
        const lit = v * v;
        data[i] = rgb[0] * lit;
        data[i + 1] = rgb[1] * lit;
        data[i + 2] = rgb[2] * lit;
        data[i + 3] = 255;
    }
    return image;
}

/// An opacity held under AMBIENT_MAX_ALPHA.
function ambientAlpha(value) {
    return Math.min(AMBIENT_MAX_ALPHA, Math.max(0, value));
}

/// A repeatable stream of numbers in [0, 1) from a seed (mulberry32).
function ambientRandom(seed) {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/// Moves a scene's sense of the music one frame on from this frame's loudness and bass (0 to 1):
/// `level` and `bass` follow them at most AMBIENT_RISE up and AMBIENT_FALL down a frame, and
/// `beat` is set for the one frame a bass hit stands clear of its recent average.
function ambientStepEnergy(state, loudness, bass) {
    if (state.level === undefined) Object.assign(state, { level: 0, bass: 0, average: 0, cooldown: 0, beat: false });

    const ease = (from, to) => (to > from ? Math.min(to, from + AMBIENT_RISE) : Math.max(to, from - AMBIENT_FALL));
    const heard = eqClamp01(loudness), low = eqClamp01(bass);
    state.level = ease(state.level, heard);
    state.bass = ease(state.bass, low);

    state.beat = false;
    if (state.cooldown > 0) state.cooldown--;
    else if (low - state.average > AMBIENT_BEAT_JUMP) {
        state.beat = true;
        state.cooldown = AMBIENT_BEAT_GAP;
    }
    state.average += (low - state.average) * 0.2;

    return state;
}

/// One style drawn on `canvas`'s 2D context. `canvas.width`/`height` are the drawing buffer, sized
/// by the caller. `seed` lays out the ambient and retro scenes; the same seed draws the same frames.
/// `createCanvas(width, height)` makes the off-page canvas the retro grain is held on.
function createEqVisualiser(canvas, { seed = AMBIENT_SEED, createCanvas = retroCanvas } = {}) {
    const ctx = canvas.getContext('2d');
    // Outlives a style switch: making the grain is the one costly step, and it depends only on the
    // seed and the colour.
    let grain = null;
    let style = null;
    let barCount = EQ_DEFAULT_BAR_COUNT;
    let scheme = 'classic';
    let colour = EQ_SINGLE_COLOUR;
    let bars = {};
    let meters = {};
    let scene = {};
    let heights = new Float32Array(barCount);
    let senseHeights = new Float32Array(16);

    function reset() {
        bars = {};
        meters = {};
        scene = {};
    }

    function gradient(x0, y0, x1, y1) {
        const g = ctx.createLinearGradient(x0, y0, x1, y1);
        for (const [at, css] of eqColourStops(scheme, colour)) g.addColorStop(at, css);
        return g;
    }

    /// Bar heights for this frame from whichever feed there is, shifted by the sensitivity's gain,
    /// into `out` (its length is the bar count).
    function barHeights(feed, out) {
        const count = out.length;

        if (feed.spectrum) {
            const s = feed.spectrum;
            eqBarsFromSpectrum(s.bytes, s.binHz, s.minDb, s.maxDb, count, out);
            const shift = (feed.gainDb || 0) / (EQ_FFT_TOP_DB - EQ_FFT_FLOOR_DB);
            for (let i = 0; i < count; i++) out[i] = eqClamp01(out[i] + (out[i] > 0 ? shift : 0));
        } else if (feed.bands) {
            const { left, right, edges } = feed.bands;
            const bandHeights = Array.from(left, (byte, i) => (eqHeightFromBandByte(byte) + eqHeightFromBandByte(right[i])) / 2);
            eqBarsFromBands(bandHeights, edges, count, out);
            const shift = (feed.gainDb || 0) / EQ_BAND_RANGE_DB;
            for (let i = 0; i < count; i++) out[i] = eqClamp01(out[i] + (out[i] > 0 ? shift : 0));
        } else {
            out.fill(0);
        }

        return out;
    }

    function drawBars(feed, mirrored) {
        const w = canvas.width, h = canvas.height;
        if (heights.length !== barCount) heights = new Float32Array(barCount);
        const state = eqStepBars(bars, barHeights(feed, heights));
        const span = w * 0.9;
        const left = (w - span) / 2;
        const pitch = span / barCount;
        const barWidth = Math.max(1, pitch * 0.78);
        const cap = Math.max(2, Math.round(h / 180));
        // Low on the screen, where the words usually are not; mirrored about the middle, shallower.
        const base = mirrored ? h * 0.5 : h * 0.96;
        const tall = mirrored ? h * 0.3 : h * 0.42;

        ctx.fillStyle = gradient(0, base, 0, base - tall);
        for (let i = 0; i < barCount; i++) {
            const bar = state.bars[i] * tall;
            if (bar > 0) ctx.fillRect(left + i * pitch, base - bar, barWidth, bar);
        }

        ctx.fillStyle = '#d8d8d8';
        for (let i = 0; i < barCount; i++) {
            ctx.fillRect(left + i * pitch, base - state.peaks[i] * tall - cap, barWidth, cap);
        }

        if (!mirrored) return;

        // The reflection, dimmer, so the half under the line reads as a reflection.
        ctx.globalAlpha = 0.45;
        ctx.fillStyle = gradient(0, base, 0, base + tall);
        for (let i = 0; i < barCount; i++) {
            const bar = state.bars[i] * tall;
            if (bar > 0) ctx.fillRect(left + i * pitch, base, barWidth, bar);
        }
        ctx.fillStyle = '#d8d8d8';
        for (let i = 0; i < barCount; i++) {
            ctx.fillRect(left + i * pitch, base + state.peaks[i] * tall, barWidth, cap);
        }
        ctx.globalAlpha = 1;
    }

    function drawScope(feed) {
        const w = canvas.width, h = canvas.height;
        const samples = feed.timeDomain;
        const middle = h * 0.74;
        const reach = h * 0.2;
        const points = Math.min(256, samples.length);
        const step = samples.length / points;
        const y = (k) => middle - ((samples[Math.floor(k * step)] - 128) / 128) * reach;

        ctx.beginPath();
        ctx.moveTo(0, y(0));
        // Through the midpoints, so the line bends rather than joins straight segments.
        for (let k = 1; k < points - 1; k++) {
            const x = (k / (points - 1)) * w;
            const nextX = ((k + 1) / (points - 1)) * w;
            ctx.quadraticCurveTo(x, y(k), (x + nextX) / 2, (y(k) + y(k + 1)) / 2);
        }
        ctx.lineTo(w, y(points - 1));

        // Coloured by how far the line strays from the middle, the way Winamp's scope was.
        const g = ctx.createLinearGradient(0, middle - reach, 0, middle + reach);
        const stops = eqColourStops(scheme, colour);
        for (const [at, css] of stops) { g.addColorStop(0.5 - at / 2, css); g.addColorStop(0.5 + at / 2, css); }

        ctx.lineJoin = 'round';
        ctx.strokeStyle = g;
        ctx.globalAlpha = 0.25;
        ctx.lineWidth = Math.max(4, h / 90);
        ctx.stroke();
        ctx.globalAlpha = 1;
        ctx.lineWidth = Math.max(2, h / 240);
        ctx.stroke();
    }

    function drawMeters(feed) {
        const w = canvas.width, h = canvas.height;
        const state = eqStepVu(meters, eqVuHeight(feed.left), eqVuHeight(feed.right));
        const left = w * 0.1, span = w * 0.82;
        const tall = h * 0.05, gap = h * 0.02;
        const top = h * 0.95 - tall * 2 - gap;
        const pitch = span / EQ_VU_SEGMENTS;
        const segment = pitch * 0.8;

        ctx.font = `bold ${Math.round(tall * 0.8)}px sans-serif`;
        ctx.textAlign = 'right';
        ctx.textBaseline = 'middle';

        for (let channel = 0; channel < 2; channel++) {
            const y = top + channel * (tall + gap);
            ctx.fillStyle = '#9a9a9a';
            ctx.fillText(channel === 0 ? 'L' : 'R', left - tall * 0.4, y + tall / 2);

            ctx.fillStyle = gradient(left, 0, left + span, 0);
            const lit = Math.round(state.levels[channel] * EQ_VU_SEGMENTS);
            const peak = Math.min(EQ_VU_SEGMENTS - 1, Math.round(state.peaks[channel] * EQ_VU_SEGMENTS) - 1);

            for (let s = 0; s < EQ_VU_SEGMENTS; s++) {
                // Unlit segments show faintly, so the meter's length reads in a quiet passage.
                ctx.globalAlpha = s < lit || s === peak ? 1 : 0.12;
                ctx.fillRect(left + s * pitch, y, segment, tall);
            }
            ctx.globalAlpha = 1;
        }
    }

    const rgba = (rgb, a) => `rgba(${rgb[0]},${rgb[1]},${rgb[2]},${Math.round(a * 1000) / 1000})`;

    /// A soft disc of light: `alpha` at the centre, `alpha` * 0.55 at `core` of the way out, clear at
    /// the rim.
    function glow(x, y, r, rgb, alpha, core) {
        if (!(alpha > 0) || !(r > 0)) return;
        const g = ctx.createRadialGradient(x, y, 0, x, y, r);
        g.addColorStop(0, rgba(rgb, alpha));
        g.addColorStop(core, rgba(rgb, alpha * 0.55));
        g.addColorStop(1, rgba(rgb, 0));
        ctx.fillStyle = g;
        ctx.beginPath();
        ctx.arc(x, y, r, 0, Math.PI * 2);
        ctx.fill();
    }

    /// A scene's moving parts, laid out from the seed the first time it is drawn after a reset.
    function buildScene() {
        const random = ambientRandom(seed ^ Math.imul(EQ_VISUALISER_STYLES.findIndex((s) => s.name === style) + 1, 0x9e3779b1));
        const make = (count, one) => Array.from({ length: count }, one);
        scene = { frame: 0, energy: {}, random, hue: random() * 5, rings: [], lastRing: -AMBIENT_RING_IDLE, ringCount: 0 };

        if (style === 'ambient-gradient') scene.items = make(3, () => ({ phase: random() * Math.PI * 2 }));
        else if (style === 'ambient-bokeh') {
            scene.items = make(22, () => ({
                x: random(), y: random(), size: 0.025 + 0.06 * random() * random(), rise: 0.0005 + 0.0012 * random(),
                sway: random() * Math.PI * 2, twinkle: random() * Math.PI * 2, hue: random() * 5,
            }));
        } else if (style === 'ambient-embers') {
            scene.items = make(64, () => ({
                x: random(), y: random(), size: 0.0025 + 0.004 * random(), rise: 0.0015 + 0.003 * random(),
                sway: random() * Math.PI * 2, hue: random() * 5,
            }));
        } else if (style === 'ambient-beams') {
            scene.items = make(5, () => ({ phase: random() * Math.PI * 2, speed: 0.12 + 0.1 * random(), reach: 0.35 + 0.1 * random() }));
        } else if (style === 'retro-vhs') {
            scene.items = make(2, (_, i) => ({ phase: random() + i * 0.5, speed: 0.045 + 0.02 * random() }));
        } else if (style === 'retro-glitch') {
            scene.items = make(16, () => ({
                x: random() * 0.92, y: random() * 0.92, w: 0.08 + 0.18 * random(), h: 0.04 + 0.1 * random(),
                hue: random() * 4, twinkle: random() * Math.PI * 2,
            }));
            Object.assign(scene, { glitch: null, lastGlitch: -RETRO_GLITCH_IDLE, glitchCount: 0 });
        } else scene.items = [];
        scene.lastTile = 0;
    }

    /// RETRO_GRAIN_TILES frames of snow in `rgb`, made once per colour from the seed; null where no
    /// canvas can be made, and the scene then draws without its grain.
    function grainTiles(rgb) {
        const key = rgb.join(',');
        if (grain && grain.key === key) return grain.tiles;

        const random = ambientRandom(seed ^ 0x67726e);
        const tiles = [];
        for (let i = 0; i < RETRO_GRAIN_TILES; i++) {
            const tile = createCanvas(RETRO_GRAIN_WIDTH, RETRO_GRAIN_HEIGHT);
            const tileCtx = tile && tile.getContext('2d');
            if (!tileCtx) { grain = { key, tiles: null }; return null; }
            tileCtx.putImageData(retroFillGrain(tileCtx.createImageData(RETRO_GRAIN_WIDTH, RETRO_GRAIN_HEIGHT), rgb, random), 0, 0);
            tiles.push(tile);
        }
        grain = { key, tiles };
        return tiles;
    }

    /// The next frame of snow: never the one just shown, chosen from the scene's own seeded stream.
    function nextTile(tiles) {
        scene.lastTile = (scene.lastTile + 1 + Math.floor(scene.random() * (tiles.length - 1))) % tiles.length;
        return tiles[scene.lastTile];
    }

    /// Grain stretched over a region, crisp rather than smeared, at an opacity under the ceiling.
    function drawGrain(tile, alpha, sy, sh, dx, dy, dw, dh) {
        if (!tile || !(alpha > 0)) return;
        ctx.imageSmoothingEnabled = false;
        ctx.globalAlpha = ambientAlpha(alpha);
        ctx.drawImage(tile, 0, sy, RETRO_GRAIN_WIDTH, sh, dx, dy, dw, dh);
        ctx.globalAlpha = 1;
        ctx.imageSmoothingEnabled = true;
    }

    /// A tube's dark lines between rows of light. Black only takes light away.
    function scanlines(alpha) {
        const w = canvas.width, h = canvas.height;
        const pitch = Math.max(2, Math.round(h / 240));
        const line = Math.max(1, Math.round(pitch / 3));
        ctx.fillStyle = `rgba(0,0,0,${alpha})`;
        for (let y = 0; y < h; y += pitch) ctx.fillRect(0, y, w, line);
    }

    /// The corners fall off into black, as a curved tube's do.
    function vignette(alpha) {
        const w = canvas.width, h = canvas.height;
        const g = ctx.createRadialGradient(w / 2, h / 2, h * 0.3, w / 2, h / 2, Math.hypot(w, h) / 2);
        g.addColorStop(0, 'rgba(0,0,0,0)');
        g.addColorStop(1, `rgba(0,0,0,${alpha})`);
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, w, h);
    }

    /// A soft horizontal band of light `tall` high centred on `y`, as a detuned set's hum bar.
    function humBar(y, tall, rgb, alpha) {
        const w = canvas.width;
        const g = ctx.createLinearGradient(0, y - tall / 2, 0, y + tall / 2);
        g.addColorStop(0, rgba(rgb, 0));
        g.addColorStop(0.5, rgba(rgb, ambientAlpha(alpha)));
        g.addColorStop(1, rgba(rgb, 0));
        ctx.fillStyle = g;
        ctx.fillRect(0, y - tall / 2, w, tall);
    }

    /// Snow on a dead channel: a fresh frame of grain each frame, a hum bar drifting down. Loudness
    /// thickens the snow, a second layer fading in over the first.
    function drawStatic(e, t, colours) {
        const w = canvas.width, h = canvas.height;
        const tiles = grainTiles(colours.grain);
        if (tiles) {
            drawGrain(nextTile(tiles), 0.16 + 0.1 * e.level, 0, RETRO_GRAIN_HEIGHT, 0, 0, w, h);
            const thicker = tiles[(scene.lastTile + (tiles.length >> 1)) % tiles.length];
            drawGrain(thicker, 0.12 * e.level, 0, RETRO_GRAIN_HEIGHT, 0, 0, w, h);
        }
        humBar(h * (((t * 0.06) % 1.4) - 0.2), h * 0.22, colours.main[0], 0.04 + 0.04 * e.bass);
        scanlines(0.3);
    }

    /// A worn tape: a dim blue wash, and tracking bands of snow rolling slowly up the screen, each
    /// torn sideways with a red and a cyan fringe. Head-switching noise along the bottom edge.
    function drawVhs(e, t, colours) {
        const w = canvas.width, h = canvas.height;
        const wash = ctx.createLinearGradient(0, 0, 0, h);
        wash.addColorStop(0, rgba(colours.main[0], ambientAlpha(0.1 + 0.06 * e.level)));
        wash.addColorStop(1, rgba(colours.main[1] || colours.main[0], ambientAlpha(0.16 + 0.08 * e.level)));
        ctx.fillStyle = wash;
        ctx.fillRect(0, 0, w, h);

        const tiles = grainTiles(colours.grain);
        const tile = tiles && nextTile(tiles);
        drawGrain(tile, 0.05, 0, RETRO_GRAIN_HEIGHT, 0, 0, w, h);

        for (const band of scene.items) {
            const tall = h * (0.03 + 0.02 * e.bass);
            const y = h * (1.15 - ((band.phase + t * band.speed) % 1.3));
            const tear = w * (0.006 + 0.01 * e.level) * Math.sin(t * 3.1 + band.phase * 7);

            // The band's own picture, pulled sideways: the tear a misaligned head leaves.
            ctx.drawImage(canvas, 0, y, w, tall, tear, y, w, tall);
            const sh = Math.max(1, Math.round(RETRO_GRAIN_HEIGHT * (tall / h)));
            drawGrain(tile, 0.24 + 0.1 * e.level, (Math.floor(scene.random() * (RETRO_GRAIN_HEIGHT - sh))), sh, tear, y, w, tall);

            const fringe = Math.max(1, h * 0.005);
            ctx.fillStyle = rgba(colours.red, ambientAlpha(0.18 + 0.08 * e.level));
            ctx.fillRect(tear + w * 0.004, y - fringe, w, fringe);
            ctx.fillStyle = rgba(colours.cyan, ambientAlpha(0.18 + 0.08 * e.level));
            ctx.fillRect(tear - w * 0.004, y + tall, w, fringe);
        }

        drawGrain(tile, 0.2, 0, Math.round(RETRO_GRAIN_HEIGHT * 0.04), w * 0.01 * Math.sin(t * 5), h * 0.965, w, h * 0.035);
        scanlines(0.25);
    }

    /// A calm tube left on: colour rolling slowly down the glass, a warm glow in the middle, a hum
    /// bar drifting through, scanlines and dark corners.
    function drawCrt(e, t, colours) {
        const w = canvas.width, h = canvas.height;
        const hue = scene.hue + t / 14;
        const wash = ctx.createLinearGradient(0, 0, 0, h);
        wash.addColorStop(0, rgba(ambientColourAt(colours.main, hue), ambientAlpha(0.1 + 0.08 * e.level)));
        wash.addColorStop(1, rgba(ambientColourAt(colours.main, hue + 1), ambientAlpha(0.1 + 0.08 * e.level)));
        ctx.fillStyle = wash;
        ctx.fillRect(0, 0, w, h);

        glow(w / 2, h / 2, h * 0.75, ambientColourAt(colours.main, hue + 0.5), ambientAlpha(0.06 + 0.1 * e.bass), 0.35);
        humBar(h * (((t * 0.05) % 1.4) - 0.2), h * 0.3, ambientColourAt(colours.main, hue + 2), 0.05 + 0.04 * e.level);
        scanlines(0.35);
        vignette(0.85);
    }

    /// Dim blocks of colour; now and then a few rows of the picture jump sideways for a moment with
    /// a red and a cyan fringe. A beat may start one, but never sooner than RETRO_GLITCH_GAP after
    /// the last, and each holds still for RETRO_GLITCH_HOLD frames rather than flickering.
    function drawGlitch(e, t, colours) {
        const w = canvas.width, h = canvas.height;
        for (const block of scene.items) {
            const twinkle = 0.5 + 0.5 * Math.sin(t * 0.5 + block.twinkle);
            ctx.fillStyle = rgba(ambientColourAt(colours.main, block.hue + t / 25), ambientAlpha(0.06 + 0.06 * twinkle + 0.1 * e.level));
            ctx.fillRect(block.x * w, block.y * h, block.w * w, block.h * h);
        }

        const since = scene.frame - scene.lastGlitch;
        if ((e.beat && since >= RETRO_GLITCH_GAP) || since >= RETRO_GLITCH_IDLE) {
            const random = scene.random;
            scene.glitch = {
                born: scene.frame,
                slices: Array.from({ length: 2 + Math.floor(random() * 2) }, () => ({
                    y: random() * 0.9, tall: 0.02 + 0.05 * random(), shift: (random() < 0.5 ? -1 : 1) * (0.03 + 0.06 * random()),
                })),
            };
            scene.lastGlitch = scene.frame;
            scene.glitchCount++;
        }

        const glitch = scene.glitch;
        const age = glitch ? scene.frame - glitch.born : RETRO_GLITCH_HOLD;
        if (age < RETRO_GLITCH_HOLD) {
            // In over two frames and out over the rest, so even the fringes never arrive at once.
            const fade = Math.min(1, (age + 1) / 2) * (1 - age / RETRO_GLITCH_HOLD);
            for (const slice of glitch.slices) {
                const y = slice.y * h, tall = slice.tall * h, shift = slice.shift * w;
                ctx.drawImage(canvas, 0, y, w, tall, shift, y, w, tall);
                const fringe = Math.max(1, tall * 0.15);
                ctx.fillStyle = rgba(colours.red, ambientAlpha(0.3 * fade));
                ctx.fillRect(shift - w * 0.006, y, w, fringe);
                ctx.fillStyle = rgba(colours.cyan, ambientAlpha(0.3 * fade));
                ctx.fillRect(shift + w * 0.006, y + tall - fringe, w, fringe);
            }
        } else scene.glitch = null;

        scanlines(0.2);
    }

    /// A test card's colour bars, dim, drifting sideways and rolling slowly up as a set that has
    /// lost its vertical hold, a dark sync bar riding the seam.
    function drawColourBars(e, t, colours) {
        const w = canvas.width, h = canvas.height;
        const bars = colours.main.length >= 7 ? colours.main : Array.from({ length: 7 }, (_, k) => ambientColourAt(colours.main, (k * colours.main.length) / 7));
        const count = bars.length;
        const roll = (scene.hue / 5 + t * 0.03) % 1;
        const drift = (scene.hue / 7 + t * 0.008) % 1;
        const alpha = ambientAlpha(0.12 + 0.1 * e.level);
        const pitch = w / count;

        for (const top of [roll * h - h, roll * h]) {
            for (let k = 0; k < count; k++) {
                const x = (((k / count + drift) % 1) * w);
                for (const at of [x, x - w]) {
                    ctx.fillStyle = rgba(bars[k], alpha);
                    ctx.fillRect(at, top, pitch + 1, h * 0.68);
                    ctx.fillStyle = rgba(bars[count - 1 - k], alpha * 0.7);
                    ctx.fillRect(at, top + h * 0.7, pitch + 1, h * 0.08);
                }
            }
        }

        const seam = roll * h;
        const sync = ctx.createLinearGradient(0, seam - h * 0.05, 0, seam + h * 0.02);
        sync.addColorStop(0, 'rgba(0,0,0,0)');
        sync.addColorStop(0.7, 'rgba(0,0,0,0.75)');
        sync.addColorStop(1, 'rgba(0,0,0,0)');
        ctx.fillStyle = sync;
        ctx.fillRect(0, seam - h * 0.05, w, h * 0.07);

        scanlines(0.25);
        vignette(0.6);
    }

    /// This frame's loudness and bass as the scene hears them: a coarse spectrum from the same feed
    /// the bars read (so the sensitivity lands the same way), smoothed so nothing jumps.
    function sense(feed) {
        barHeights(feed, senseHeights);
        let all = 0, low = 0;
        for (let i = 0; i < senseHeights.length; i++) all += senseHeights[i];
        for (let i = 0; i < 4; i++) low += senseHeights[i];
        return ambientStepEnergy(scene.energy, all / senseHeights.length, low / 4);
    }

    /// Three wide pools of colour wandering slowly, their hues turning round the palette.
    function drawDrift(e, t, palette) {
        const w = canvas.width, h = canvas.height;
        const reach = Math.hypot(w, h) * (0.42 + 0.04 * e.bass);
        scene.items.forEach((pool, i) => {
            const x = w * (0.5 + 0.4 * Math.sin(t * (0.05 + i * 0.017) + pool.phase));
            const y = h * (0.5 + 0.32 * Math.cos(t * (0.043 + i * 0.011) + pool.phase * 1.7));
            glow(x, y, reach, ambientColourAt(palette, (i * palette.length) / 3 + t / 25), ambientAlpha(0.16 + 0.12 * e.level), 0.4);
        });
    }

    /// Out-of-focus lights rising slowly and twinkling; the music lifts them a little faster and
    /// brighter, and a bass note swells them slightly.
    function drawBokeh(e, t, palette) {
        const w = canvas.width, h = canvas.height;
        for (const orb of scene.items) {
            orb.y -= orb.rise * (1 + 0.8 * e.level);
            if (orb.y < -orb.size * 2) { orb.y = 1 + orb.size * 2; orb.x = scene.random(); }

            const twinkle = 0.5 + 0.5 * Math.sin(t * 0.6 + orb.twinkle);
            const x = (orb.x + 0.03 * Math.sin(t * 0.2 + orb.sway)) * w;
            glow(x, orb.y * h, orb.size * h * (1 + 0.1 * e.bass), ambientColourAt(palette, orb.hue),
                ambientAlpha(0.1 + 0.08 * twinkle + 0.15 * e.level), 0.75);
        }
    }

    /// Sparks rising from a faint glow along the bottom, fading out before they reach the top.
    function drawEmbers(e, t, palette) {
        const w = canvas.width, h = canvas.height;
        const warm = ctx.createLinearGradient(0, h * 0.7, 0, h);
        const base = ambientColourAt(palette, t / 30);
        warm.addColorStop(0, rgba(base, 0));
        warm.addColorStop(1, rgba(base, ambientAlpha(0.08 + 0.12 * e.bass)));
        ctx.fillStyle = warm;
        ctx.fillRect(0, h * 0.7, w, h * 0.3);

        for (const spark of scene.items) {
            spark.y -= spark.rise * (1 + 0.9 * e.level);
            if (spark.y < -0.02) { spark.y = 1.02; spark.x = scene.random(); }

            const fade = eqClamp01(spark.y * 1.3);
            const alpha = ambientAlpha((0.22 + 0.18 * e.level) * fade);
            const x = (spark.x + 0.015 * Math.sin(t * 0.8 + spark.sway)) * w;
            const rgb = ambientColourAt(palette, spark.hue);
            glow(x, spark.y * h, spark.size * h * 5, rgb, alpha * 0.35, 0.2);
            glow(x, spark.y * h, spark.size * h * 1.5, rgb, alpha, 0.6);
        }
    }

    /// Thin rings widening from the middle and fading: one on each beat, and a slower one when
    /// there is none, so a quiet passage still breathes.
    function drawRings(e, t, palette) {
        const w = canvas.width, h = canvas.height;
        const cx = w / 2, cy = h / 2;

        if (e.beat || scene.frame - scene.lastRing >= AMBIENT_RING_IDLE) {
            scene.rings.push({ born: scene.frame, strength: e.beat ? 1 : 0.8, rgb: ambientColourAt(palette, scene.hue + scene.ringCount * 1.3) });
            scene.lastRing = scene.frame;
            scene.ringCount++;
        }
        scene.rings = scene.rings.filter((ring) => scene.frame - ring.born < AMBIENT_RING_LIFE);

        glow(cx, cy, h * (0.18 + 0.06 * e.bass), ambientColourAt(palette, scene.hue + t / 20), ambientAlpha(0.06 + 0.12 * e.bass), 0.3);

        for (const ring of scene.rings) {
            const age = (scene.frame - ring.born) / AMBIENT_RING_LIFE;
            const radius = h * (0.05 + 0.6 * (1 - (1 - age) * (1 - age)));
            const alpha = ambientAlpha(0.34 * ring.strength * Math.pow(1 - age, 1.5));
            const width = Math.max(1.5, h * 0.006 * (1.6 - age));

            ctx.beginPath();
            ctx.arc(cx, cy, radius, 0, Math.PI * 2);
            ctx.strokeStyle = rgba(ring.rgb, alpha * 0.25);
            ctx.lineWidth = width * 4;
            ctx.stroke();
            ctx.strokeStyle = rgba(ring.rgb, alpha);
            ctx.lineWidth = width;
            ctx.stroke();
        }
    }

    /// Stage lights along the bottom edge, their beams sweeping slowly; the music quickens the sweep
    /// and lifts the light a little.
    function drawBeams(e, t, palette) {
        const w = canvas.width, h = canvas.height;
        const length = h * 1.4;
        const alpha = ambientAlpha(0.13 + 0.12 * e.level + 0.06 * e.bass);

        scene.items.forEach((beam, i) => {
            beam.phase += (beam.speed * (1 + 0.6 * e.level)) / 30;
            const angle = Math.sin(beam.phase) * beam.reach;
            const ox = w * (0.1 + (0.8 * i) / (scene.items.length - 1)), oy = h * 1.02;
            const dx = Math.sin(angle), dy = -Math.cos(angle);
            const tipX = ox + dx * length, tipY = oy + dy * length;
            const spread = length * 0.1, foot = h * 0.01;
            const rgb = ambientColourAt(palette, i + t / 30);

            const g = ctx.createLinearGradient(ox, oy, tipX, tipY);
            g.addColorStop(0, rgba(rgb, alpha));
            g.addColorStop(1, rgba(rgb, 0));
            ctx.fillStyle = g;
            ctx.beginPath();
            ctx.moveTo(ox - dy * foot, oy + dx * foot);
            ctx.lineTo(tipX - dy * spread, tipY + dx * spread);
            ctx.lineTo(tipX + dy * spread, tipY - dx * spread);
            ctx.lineTo(ox + dy * foot, oy - dx * foot);
            ctx.closePath();
            ctx.fill();

            glow(ox, oy, h * 0.08, rgb, alpha, 0.3);
        });
    }

    /// One frame of a calm scene: its motion steps one frame whatever the music, and the music only
    /// nudges how fast and how bright.
    function drawScene(feed) {
        if (!scene.items) buildScene();
        const e = sense(feed);
        const t = scene.frame / 30;
        const palette = isRetroStyle(style) ? retroColours(style, scheme, colour) : ambientPalette(scheme, colour);

        if (style === 'ambient-gradient') drawDrift(e, t, palette);
        else if (style === 'ambient-bokeh') drawBokeh(e, t, palette);
        else if (style === 'ambient-embers') drawEmbers(e, t, palette);
        else if (style === 'ambient-rings') drawRings(e, t, palette);
        else if (style === 'ambient-beams') drawBeams(e, t, palette);
        else if (style === 'retro-static') drawStatic(e, t, palette);
        else if (style === 'retro-vhs') drawVhs(e, t, palette);
        else if (style === 'retro-crt') drawCrt(e, t, palette);
        else if (style === 'retro-glitch') drawGlitch(e, t, palette);
        else if (style === 'retro-bars') drawColourBars(e, t, palette);

        scene.frame++;
    }

    return {
        /// Answers whether the style exists; a new one starts its bars and meters from rest.
        setStyle(name) {
            if (!isEqVisualiserStyle(name)) return false;
            if (name !== style) reset();
            style = name;
            return true;
        },

        /// Bar count (snapped to EQ_BAR_COUNTS), colour scheme ('classic', 'theme' or 'single')
        /// and the single colour ('#rrggbb').
        setOptions({ barCount: count, colourScheme, colour: single } = {}) {
            barCount = eqBarCount(count);
            scheme = colourScheme === 'theme' || colourScheme === 'single' ? colourScheme : 'classic';
            colour = eqParseColour(single) ? single : EQ_SINGLE_COLOUR;
        },

        /// One frame. `feed` carries `timeDomain`, `left` and `right` (byte samples, 128 silent),
        /// `gainDb` (the sensitivity's shift) and one of `spectrum` ({ bytes, binHz, minDb, maxDb },
        /// from a live tap) or `bands` ({ left, right, edges }, a frame of the host's levels).
        draw(feed) {
            ctx.globalAlpha = 1;
            ctx.fillStyle = '#000';
            ctx.fillRect(0, 0, canvas.width, canvas.height);

            if (style === 'spectrum-bars') drawBars(feed, false);
            else if (style === 'mirrored-bars') drawBars(feed, true);
            else if (style === 'oscilloscope') drawScope(feed);
            else if (style === 'vu-meters') drawMeters(feed);
            else if (isAmbientStyle(style) || isRetroStyle(style)) drawScene(feed);
        },

        reset,

        get style() { return style; },
        get barCount() { return barCount; },
        get colourScheme() { return scheme; },
        get state() { return { bars, meters, scene }; },
    };
}

