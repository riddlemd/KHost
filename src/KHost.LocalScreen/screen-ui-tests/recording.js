// Records what the screen's 2D drawers ask of a canvas, so a change to how often something is
// computed can be held to drawing exactly what it drew before. Each draw call carries the state it
// was drawn with, not the sets that led there: a cached value set once draws the same as one set twice.

'use strict';

const { loadScreen, stubElement } = require('./load-screen.js');

const STATE_KEYS = ['fillStyle', 'strokeStyle', 'globalAlpha', 'font', 'lineWidth', 'lineJoin', 'textAlign', 'textBaseline', 'imageSmoothingEnabled'];
const DRAWS = new Set(['fill', 'stroke', 'fillRect', 'strokeRect', 'fillText', 'strokeText', 'drawImage', 'clearRect', 'putImageData', 'clip']);
const INITIAL_STATE = {
    fillStyle: '#000000', strokeStyle: '#000000', globalAlpha: 1, font: '10px sans-serif', lineWidth: 1,
    lineJoin: 'miter', textAlign: 'start', textBaseline: 'alphabetic', imageSmoothingEnabled: true,
};

/// A width that depends on the font's size and on each character, so a stale font or a
/// mis-keyed width shows up as a different number.
function measure(font, text) {
    const px = Number((/([\d.]+)px/.exec(font) || [0, 10])[1]);
    let width = 0;
    for (const ch of String(text)) width += px * (0.4 + (ch.codePointAt(0) % 7) * 0.05);
    return width;
}

function hashBytes(bytes) {
    let h = 2166136261;
    for (let i = 0; i < bytes.length; i++) h = Math.imul(h ^ bytes[i], 16777619) >>> 0;
    return h;
}

/// A canvas whose 2D context writes every call into `log.ops` (while `log.on`) and counts every
/// call in `log.counts` (always). `tag` names it when another canvas draws it.
function recordingCanvas(log, tag, { width = 0, height = 0, clientWidth = 0, clientHeight = 0 } = {}) {
    const canvas = stubElement({ width, height, clientWidth, clientHeight, tag });
    const state = { ...INITIAL_STATE };
    const stack = [];

    const describe = (value) => {
        if (value === canvas) return { canvas: 'self' };
        if (value && value.tag) return { canvas: value.tag };
        if (value && value.gradient) return value.gradient;
        if (value && value.data && value.width) return { image: [value.width, value.height, hashBytes(value.data)] };
        return value;
    };

    const count = (name) => { log.counts[name] = (log.counts[name] || 0) + 1; };
    const record = (name, args) => {
        if (!log.on) return;
        const op = { op: name, args: args.map(describe) };
        if (DRAWS.has(name)) op.state = Object.fromEntries(STATE_KEYS.map((k) => [k, describe(state[k])]));
        log.ops.push(op);
    };

    const gradient = (type, args) => {
        const g = { gradient: { g: type, args: [...args], stops: [] } };
        g.addColorStop = (at, css) => { g.gradient.stops.push([at, css]); };
        return g;
    };

    const methods = {
        save() { stack.push({ ...state }); },
        restore() { if (stack.length) Object.assign(state, stack.pop()); },
        measureText(text) { return { width: measure(state.font, text) }; },
        createLinearGradient(...args) { return gradient('linear', args); },
        createRadialGradient(...args) { return gradient('radial', args); },
        createImageData(w, h) { return { width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }; },
    };

    const ctx = new Proxy({}, {
        get(_, key) {
            if (STATE_KEYS.includes(key)) return state[key];
            if (typeof key !== 'string') return undefined;
            return (...args) => {
                count(key);
                // A canvas's context hands the same 2D context back; a test needs only that.
                if (key !== 'measureText' && !key.startsWith('create')) record(key, args);
                return methods[key] ? methods[key](...args) : undefined;
            };
        },
        set(_, key, value) {
            if (STATE_KEYS.includes(key)) state[key] = value;
            return true;
        },
    });

    canvas.getContext = (kind) => (kind === '2d' ? ctx : null);
    return canvas;
}

/// A fresh log: `ops` holds what was drawn while `on`, `counts` every call by name.
function newLog() {
    return { on: true, ops: [], counts: {} };
}

// ---- The visualiser's scenario -------------------------------------------------------------

const EQ_STYLES = [
    'spectrum-bars', 'mirrored-bars', 'oscilloscope', 'vu-meters',
    'ambient-gradient', 'ambient-bokeh', 'ambient-embers', 'ambient-rings', 'ambient-beams', 'ambient-clouds',
    'retro-static', 'retro-vhs', 'retro-crt', 'retro-glitch', 'retro-bars',
];

const EQ_FRAMES = 60;
const EQ_KEPT_FRAMES = [0, 1, 20, 30, 40, 41, 50, 59];

/// One frame's feed: host bands for the first half, a live spectrum for the second, with a bass
/// hit every 18 frames so the beat-driven scenes have beats.
function eqFeed(screen, k) {
    const wave = (n, f, a) => Uint8Array.from({ length: n }, (_, i) => Math.round(128 + a * Math.sin(i * f + k * 0.37)));
    const hit = k % 18 === 0;
    const feed = { timeDomain: wave(1024, 0.05, 90), left: wave(1024, 0.07, 70), right: wave(1024, 0.11, 40), gainDb: k % 3 };
    if (k < EQ_FRAMES / 2) {
        const band = (i, phase) => (hit && i < 3 ? 255 : Math.round(160 + 90 * Math.sin(k * 0.21 + i * phase)));
        feed.bands = {
            left: Uint8Array.from({ length: 8 }, (_, i) => band(i, 0.9)),
            right: Uint8Array.from({ length: 8 }, (_, i) => band(i, 1.3)),
            edges: screen.$('VISUALISER_BAND_EDGES'),
        };
    } else {
        feed.spectrum = {
            bytes: Uint8Array.from({ length: 1024 }, (_, i) => (hit && i < 12 ? 250 : Math.round(120 + 100 * Math.sin(i * 0.013 + k * 0.29) * Math.exp(-i / 600)))),
            binHz: 22050 / 1024, minDb: -100, maxDb: -30,
        };
    }
    return feed;
}

/// Runs one style through EQ_FRAMES frames, changing palette, bar count and canvas size on the
/// way, so every cache has to notice each. `perFrame(k, log)` sees each frame's counts; `made`
/// collects the off-page canvases, each with its own log, so making one is not part of a frame.
function runEq(style, { perFrame, keep = EQ_KEPT_FRAMES, made = [] } = {}) {
    const screen = loadScreen();
    const log = newLog();
    const createCanvas = (width, height) => {
        const canvasLog = newLog();
        const madeCanvas = recordingCanvas(canvasLog, `made#${made.length}`, { width, height });
        madeCanvas.log = canvasLog;
        made.push(madeCanvas);
        return madeCanvas;
    };
    const canvas = recordingCanvas(log, 'eq', { width: 1280, height: 720 });
    const eq = screen.createEqVisualiser(canvas, { seed: 1234, createCanvas });
    eq.setStyle(style);
    eq.setOptions({ barCount: 32, colourScheme: 'classic' });

    const frames = {};
    for (let k = 0; k < EQ_FRAMES; k++) {
        if (k === 20) eq.setOptions({ barCount: 64, colourScheme: 'single', colour: '#ff8800' });
        if (k === 30) eq.setOptions({ barCount: 64, colourScheme: 'theme' });
        if (k === 40) { canvas.width = 640; canvas.height = 360; }
        if (k === 50) eq.setOptions({ barCount: 16, palette: ['#112233', '#a0c0e0', '#301020'] });

        log.ops = [];
        log.counts = {};
        log.on = keep.includes(k);
        eq.draw(eqFeed(screen, k));
        if (log.on) frames[k] = log.ops;
        if (perFrame) perFrame(k, log);
    }
    return frames;
}

// ---- The words overlay's scenario ----------------------------------------------------------

const LYRICS = {
    bounds: { width: 640, height: 360 },
    countIns: [
        { position: { x: 200, y: 150, width: 240, height: 12 }, startSeconds: 0, endSeconds: 4, stepSeconds: 1, steps: 3, inactive: { r: 10, g: 20, b: 30 }, borderWidth: 1 },
        { position: { x: 200, y: 150, width: 240, height: 12 }, startSeconds: 14, endSeconds: 16.5, stepSeconds: 0.5, steps: 2, active: { r: 0, g: 90, b: 200 }, borderWidth: 0 },
    ],
    pages: [
        {
            showFromSeconds: 3.5, showUntilSeconds: 13, active: { r: 255, g: 0, b: 0 },
            lines: [
                {
                    position: { x: 40, y: 200, width: 560, height: 48 },
                    leadIn: { startSeconds: 3.5, x: 10, arriveAtSyllable: 0 },
                    syllables: [
                        { text: 'Hel', startSeconds: 4, endSeconds: 4.5 },
                        { text: 'lo', startSeconds: 4.5, endSeconds: 5 },
                        { text: '', startSeconds: 5, endSeconds: 5 },
                        { text: ' world', startSeconds: 5, endSeconds: 6 },
                        { text: ' now', startSeconds: 6.2, endSeconds: 6.2 },
                    ],
                },
                {
                    leadIn: { startSeconds: 6, x: 20, arriveAtSyllable: 2 },
                    position: { x: 40, y: 250, width: 560, height: 40 },
                    syllables: [
                        { text: 'Some', startSeconds: 6.5, endSeconds: 7 },
                        { text: 'thing', startSeconds: 7, endSeconds: 7.4 },
                        { text: ' long', startSeconds: 8, endSeconds: 8.6 },
                        { text: 'er', startSeconds: 8.6, endSeconds: 9 },
                        { text: ' here', startSeconds: 9, endSeconds: 10 },
                    ],
                },
                {
                    syllables: [
                        { text: 'stacked', startSeconds: 10, endSeconds: 10.5 },
                        { text: ' un', startSeconds: 10.5, endSeconds: 11 },
                        { text: 'der', startSeconds: 11, endSeconds: 11.5 },
                    ],
                },
            ],
        },
        {
            showFromSeconds: 12.5, showUntilSeconds: 22, inactive: { r: 200, g: 200, b: 160 },
            lines: [
                {
                    position: { x: 60, y: 120, width: 180, height: 44 },
                    leadIn: { startSeconds: 15, x: 0, arriveAtSyllable: 1 },
                    syllables: [
                        { text: 'Squeezed', startSeconds: 16, endSeconds: 16.8 },
                        { text: ' into', startSeconds: 17, endSeconds: 17.5 },
                        { text: ' a narrow box', startSeconds: 17.5, endSeconds: 19 },
                    ],
                },
                {
                    position: { x: 60, y: 180, width: 500, height: 36 },
                    leadIn: { startSeconds: 18.5, x: 0, arriveAtSyllable: 0 },
                    syllables: [
                        { text: 'Last', startSeconds: 19.5, endSeconds: 20 },
                        { text: ' line', startSeconds: 20, endSeconds: 21 },
                    ],
                },
            ],
        },
    ],
};

const LYRIC_TIMES = [null, 0, 0.5, 1.2, 2.5, 3.2, 3.6, 3.9, 4.2, 4.7, 5.5, 6.1, 6.5, 6.9, 7.3, 8, 8.8, 9.5, 10.2, 11.2, 12.2,
    12.8, 14.2, 15, 15.6, 16.1, 16.4, 17, 17.6, 18.3, 18.9, 19.7, 20.5, 21.5, 23];

/// Runs the overlay over LYRIC_TIMES, one frame each, with the venue's colours changing and the
/// window resizing part way, so a layout or colour cache has to notice each.
function runLyrics({ rightToLeft = false, perFrame } = {}) {
    const screen = loadScreen();
    const pending = [];
    screen.requestAnimationFrame = (cb) => { pending.push(cb); return pending.length; };
    screen.cancelAnimationFrame = () => {};
    const log = newLog();
    const canvas = recordingCanvas(log, 'lyrics', { clientWidth: 1280, clientHeight: 720 });
    let now = null;
    const overlay = screen.createLyricsOverlay(canvas, () => now);
    overlay.setLyrics({ ...LYRICS, isRightToLeft: rightToLeft });

    const frames = [];
    LYRIC_TIMES.forEach((t, k) => {
        if (k === 12) overlay.setColours({ sung: '#00ff88', unsung: '#eeeeaa', outline: '#202040' });
        if (k === 24) { canvas.clientWidth = 960; canvas.clientHeight = 720; }
        if (k === 30) overlay.setColours({});
        now = t;
        log.ops = [];
        log.counts = {};
        log.on = true;
        pending.shift()(0);
        frames.push(log.ops);
        if (perFrame) perFrame(k, log);
    });
    return frames;
}

/// Frames as stored: each op [name, args, state index], the distinct states listed once.
function compact(frames) {
    const states = [], index = new Map();
    const pack = (ops) => ops.map((o) => {
        if (!o.state) return [o.op, o.args];
        const key = JSON.stringify(o.state);
        if (!index.has(key)) { index.set(key, states.length); states.push(o.state); }
        return [o.op, o.args, index.get(key)];
    });
    const out = Array.isArray(frames) ? frames.map(pack) : Object.fromEntries(Object.entries(frames).map(([k, ops]) => [k, pack(ops)]));
    return { states, frames: out };
}

/// The inverse of compact, as plain data.
function expand({ states, frames }) {
    const unpack = (ops) => ops.map(([op, args, i]) => (i === undefined ? { op, args } : { op, args, state: states[i] }));
    return Array.isArray(frames) ? frames.map(unpack) : Object.fromEntries(Object.entries(frames).map(([k, ops]) => [k, unpack(ops)]));
}

module.exports = { compact, expand, eqFeed,  recordingCanvas, newLog, runEq, runLyrics, EQ_STYLES, EQ_FRAMES, LYRIC_TIMES, measure };
