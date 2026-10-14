'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadScreen } = require('./load-screen.js');

test('eqBarsFromBands_RisingBands_GivesRisingBarsHeldToTheEndBandsAndClamped', () => {
    const screen = loadScreen();
    const edges = screen.$('VISUALISER_BAND_EDGES');
    const bands = [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 1.5];

    const bars = Array.from(screen.eqBarsFromBands(bands, edges, 32));

    assert.equal(bars[0], 0, 'below the lowest band centre takes that band');
    assert.equal(bars[31], 1, 'above the highest takes it, clamped to 1');
    for (let i = 1; i < bars.length; i++) assert.ok(bars[i] >= bars[i - 1], `bar ${i} does not dip`);
    const distinct = new Set(bars.map((v) => v.toFixed(4)));
    assert.ok(distinct.size > bands.length, 'bars between band centres interpolate rather than step');
});

test('eqStepBars_AfterAPeak_BarFallsAtMostOneStepAndCapHoldsThenFalls', () => {
    const screen = loadScreen();
    const fall = screen.$('EQ_BAR_FALL');
    const hold = screen.$('EQ_PEAK_HOLD_FRAMES');
    const peakFall = screen.$('EQ_PEAK_FALL');
    const state = {};

    screen.eqStepBars(state, [1]);
    screen.eqStepBars(state, [0]);
    assert.ok(Math.abs(state.bars[0] - (1 - fall)) < 1e-6, 'bar falls one step, not to zero');
    assert.equal(state.peaks[0], 1, 'cap holds');

    for (let i = 1; i < hold; i++) screen.eqStepBars(state, [0]);
    assert.equal(state.peaks[0], 1, 'cap still held through its hold');

    screen.eqStepBars(state, [0]);
    assert.ok(Math.abs(state.peaks[0] - (1 - peakFall)) < 1e-6, 'cap falls once the hold runs out');
});
