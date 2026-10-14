'use strict';

// What the screen's 2D drawers draw, held to golden/ (recorded by record-goldens.js). The words
// overlay must draw what the burn-in painter draws, so a change here is a change to both.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { loadScreen } = require('./load-screen.js');
const { runEq, runLyrics, expand, eqFeed, recordingCanvas, newLog, EQ_STYLES } = require('./recording.js');

const golden = (name) => expand(JSON.parse(fs.readFileSync(path.join(__dirname, 'golden', name), 'utf8')));
const plain = (value) => JSON.parse(JSON.stringify(value));

/// The first op that differs, so a failure names a frame and a call rather than dumping both.
function assertSameFrames(actual, expected) {
    const keys = Object.keys(expected);
    assert.deepEqual(Object.keys(actual), keys, 'the same frames were recorded');
    for (const k of keys) {
        const a = actual[k], e = expected[k];
        const n = Math.max(a.length, e.length);
        for (let i = 0; i < n; i++) assert.deepEqual(a[i], e[i], `frame ${k}, op ${i} of ${e.length}`);
    }
}

for (const style of EQ_STYLES.filter((s) => s !== 'ambient-embers')) {
    test(`createEqVisualiser_${style}_DrawsWhatTheGoldenRecorded`, () => {
        assertSameFrames(plain(runEq(style)), golden(`eq-${style}.json`));
    });
}

const RGBA = /^rgba\((\d+),(\d+),(\d+),([\d.e-]+)\)$/;

/// A gradient's colour and each stop's alpha as it lands, faded by `alpha`.
function colourOf(stops, alpha) {
    const parsed = stops.map(([at, css]) => [at, RGBA.exec(css).slice(1).map(Number)]);
    return { rgb: parsed[0][1].slice(0, 3), stops: parsed.map(([at, c]) => [at, c[3] * alpha]) };
}

/// A frame with each spark's glow as { x, y, r, rgb, stops: [at, alpha] }, however it was drawn:
/// an arc filled with a radial gradient, or a drawn image of a sprite holding that gradient.
function glowsOf(ops, made) {
    const glows = [], rest = [];
    for (let i = 0; i < ops.length; i++) {
        const [a, b, c] = [ops[i], ops[i + 1], ops[i + 2]];
        const fill = c && c.state && c.state.fillStyle;
        if (a.op === 'beginPath' && b && b.op === 'arc' && c.op === 'fill' && fill && fill.g === 'radial') {
            glows.push({ x: b.args[0], y: b.args[1], r: b.args[2], ...colourOf(fill.stops, 1) });
            i += 2;
            continue;
        }
        const source = a.op === 'drawImage' && a.args[0] && made.find((m) => m.tag === a.args[0].canvas);
        if (source) {
            const sprite = source.log.ops.find((o) => o.state && o.state.fillStyle && o.state.fillStyle.g === 'radial').state.fillStyle;
            const [, dx, dy, dw] = a.args;
            glows.push({ x: dx + dw / 2, y: dy + dw / 2, r: dw / 2, ...colourOf(sprite.stops, a.state.globalAlpha) });
            continue;
        }
        rest.push(a);
    }
    return { glows, rest };
}

test('createEqVisualiser_AmbientEmbers_DrawsEachSparkWhereTheGoldenDid', () => {
    const made = [];
    const actual = plain(runEq('ambient-embers', { made }));
    const expected = golden('eq-ambient-embers.json');

    for (const k of Object.keys(expected)) {
        const now = glowsOf(actual[k], made), then = glowsOf(expected[k], []);
        assert.deepEqual(now.rest, then.rest, `frame ${k}: everything but the sparks`);
        assert.equal(now.glows.length, then.glows.length, `frame ${k}: as many glows`);
        then.glows.forEach((e, i) => {
            const a = now.glows[i];
            const where = `frame ${k}, glow ${i}`;
            for (const key of ['x', 'y', 'r']) assert.ok(Math.abs(a[key] - e[key]) < 1e-9, `${where}: ${key} ${a[key]} vs ${e[key]}`);
            assert.deepEqual(a.rgb, e.rgb, `${where}: colour`);
            assert.deepEqual(a.stops.map((s) => s[0]), e.stops.map((s) => s[0]), `${where}: stop positions`);
            // The golden wrote each alpha into a CSS string to three places.
            a.stops.forEach(([, alpha], s) => assert.ok(Math.abs(alpha - e.stops[s][1]) <= 0.0005 + 1e-12, `${where}, stop ${s}: alpha ${alpha} vs ${e.stops[s][1]}`));
        });
    }
});

test('createLyricsOverlay_LeftToRight_DrawsWhatTheGoldenRecorded', () => {
    assertSameFrames(plain(runLyrics()), golden('lyrics-ltr.json'));
});

test('createLyricsOverlay_RightToLeft_DrawsWhatTheGoldenRecorded', () => {
    assertSameFrames(plain(runLyrics({ rightToLeft: true })), golden('lyrics-rtl.json'));
});

test('createEqVisualiser_SwitchedFromARetroSceneToAnAmbientOne_DrawsAsAFreshOneWould', () => {
    const frames = (before) => {
        const screen = loadScreen();
        const log = newLog();
        const eq = screen.createEqVisualiser(recordingCanvas(log, 'eq', { width: 1280, height: 720 }), { seed: 1234, createCanvas: () => null });
        eq.setOptions({ colourScheme: 'classic' });
        if (before) { eq.setStyle(before); for (let k = 0; k < 3; k++) eq.draw(eqFeed(screen, k)); }
        eq.setStyle('ambient-gradient');
        log.ops = [];
        for (let k = 0; k < 3; k++) eq.draw(eqFeed(screen, k));
        return plain(log.ops);
    };
    assert.deepEqual(frames('retro-crt'), frames(null));
});

// ---- How much is made per frame ------------------------------------------------------------

/// Frames where runEq changes palette, bar count or size: a cache may rebuild on those.
const EQ_CHANGES = new Set([0, 20, 30, 40, 50]);

function steadyCounts(style, names) {
    const totals = Object.fromEntries(names.map((n) => [n, 0]));
    runEq(style, {
        keep: [],
        perFrame: (k, log) => { if (!EQ_CHANGES.has(k)) for (const n of names) totals[n] += log.counts[n] || 0; },
    });
    return totals;
}

for (const style of ['spectrum-bars', 'mirrored-bars', 'oscilloscope', 'vu-meters']) {
    test(`createEqVisualiser_${style}_MakesNoGradientBetweenSizeOrPaletteChanges`, () => {
        assert.deepEqual(steadyCounts(style, ['createLinearGradient', 'createRadialGradient']), { createLinearGradient: 0, createRadialGradient: 0 });
    });
}

test('createEqVisualiser_AmbientEmbers_MakesNoRadialGradientBetweenPaletteChanges', () => {
    assert.equal(steadyCounts('ambient-embers', ['createRadialGradient']).createRadialGradient, 0);
});

test('createEqVisualiser_AmbientEmbers_RemakesItsSpritesOnAPaletteChange', () => {
    const made = [];
    const perFrame = [];
    runEq('ambient-embers', { keep: [], made, perFrame: (k) => { perFrame[k] = made.length; } });
    assert.ok(perFrame[19] > 0, 'sprites are made');
    assert.equal(perFrame[19], perFrame[1], 'none made while the palette holds');
    assert.ok(perFrame[20] > perFrame[19], 'a palette change makes them again');
});

test('createLyricsOverlay_MeasuresOnlyWhenAPageArrivesOrTheScaleChanges', () => {
    const measuring = [];
    runLyrics({ perFrame: (k, log) => { if (log.counts.measureText) measuring.push(k); } });
    // 6: the first page shows; 21: the second; 24: the window is resized.
    assert.deepEqual(measuring, [6, 21, 24]);
});
