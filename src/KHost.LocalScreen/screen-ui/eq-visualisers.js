// The host's own drawings under the words: a spectrum analyser, the same mirrored, an
// oscilloscope and a pair of VU meters. Canvas 2D, no third-party code. visualiser.js decides when
// one is up and hands each frame what it heard; this only turns that into a picture.
//
// Everything sits low on the screen, under where the words usually are, so the picture behind a
// line stays dark. Frame-counted motion (peak holds, falls) assumes visualiser.js's 30fps cap.

/// The styles a playlist entry can name, by the name the host sends. VisualiserPresetService
/// mirrors this list; a test holds them together.
const EQ_VISUALISER_STYLES = [
    { name: 'spectrum-bars', title: 'Spectrum bars' },
    { name: 'mirrored-bars', title: 'Mirrored bars' },
    { name: 'oscilloscope', title: 'Oscilloscope' },
    { name: 'vu-meters', title: 'Twin VU meters' },
];

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

/// One style drawn on `canvas`'s 2D context. `canvas.width`/`height` are the drawing buffer, sized
/// by the caller.
function createEqVisualiser(canvas) {
    const ctx = canvas.getContext('2d');
    let style = null;
    let barCount = EQ_DEFAULT_BAR_COUNT;
    let scheme = 'classic';
    let colour = EQ_SINGLE_COLOUR;
    let bars = {};
    let meters = {};
    let heights = new Float32Array(barCount);

    function reset() {
        bars = {};
        meters = {};
    }

    function gradient(x0, y0, x1, y1) {
        const g = ctx.createLinearGradient(x0, y0, x1, y1);
        for (const [at, css] of eqColourStops(scheme, colour)) g.addColorStop(at, css);
        return g;
    }

    /// Bar heights for this frame from whichever feed there is, shifted by the sensitivity's gain.
    function barHeights(feed) {
        if (heights.length !== barCount) heights = new Float32Array(barCount);

        if (feed.spectrum) {
            const s = feed.spectrum;
            eqBarsFromSpectrum(s.bytes, s.binHz, s.minDb, s.maxDb, barCount, heights);
            const shift = (feed.gainDb || 0) / (EQ_FFT_TOP_DB - EQ_FFT_FLOOR_DB);
            for (let i = 0; i < barCount; i++) heights[i] = eqClamp01(heights[i] + (heights[i] > 0 ? shift : 0));
        } else if (feed.bands) {
            const { left, right, edges } = feed.bands;
            const bandHeights = Array.from(left, (byte, i) => (eqHeightFromBandByte(byte) + eqHeightFromBandByte(right[i])) / 2);
            eqBarsFromBands(bandHeights, edges, barCount, heights);
            const shift = (feed.gainDb || 0) / EQ_BAND_RANGE_DB;
            for (let i = 0; i < barCount; i++) heights[i] = eqClamp01(heights[i] + (heights[i] > 0 ? shift : 0));
        } else {
            heights.fill(0);
        }

        return heights;
    }

    function drawBars(feed, mirrored) {
        const w = canvas.width, h = canvas.height;
        const state = eqStepBars(bars, barHeights(feed));
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
        },

        reset,

        get style() { return style; },
        get barCount() { return barCount; },
        get colourScheme() { return scheme; },
        get state() { return { bars, meters }; },
    };
}

