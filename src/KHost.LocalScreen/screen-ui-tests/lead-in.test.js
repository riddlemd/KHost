'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadScreen, plain } = require('./load-screen.js');

const page = (at) => ({ showFromSeconds: at, showUntilSeconds: at + 5, lines: [] });

test('withLeadInCountIn_SongOpensWithCountIn_StartsThatOneEarlierInsteadOfAddingOne', () => {
    const screen = loadScreen();
    const opening = { startSeconds: 0.5, endSeconds: 1, steps: 1 };
    const second = { startSeconds: 1, endSeconds: 2, steps: 1 };
    const later = { startSeconds: 30, endSeconds: 32, steps: 2 };
    const lyrics = { pages: [page(2), page(40)], countIns: [later, second, opening] };

    const led = plain(screen.withLeadInCountIn(lyrics, 3));

    assert.deepEqual(led.countIns, [later, second, { ...opening, startSeconds: -3 }], 'only the earliest moves');
    assert.equal(screen.withLeadInCountIn(lyrics, 0), lyrics, 'no hold leaves the timing as it was');
});

test('withLeadInCountIn_SongWithNoCountIn_AddsOneBarToTheFirstPage', () => {
    const screen = loadScreen();
    const lyrics = { pages: [page(5), page(1.5)], countIns: [] };

    const led = plain(screen.withLeadInCountIn(lyrics, 2));

    assert.equal(led.countIns.length, 1);
    const [bar] = led.countIns;
    assert.deepEqual([bar.startSeconds, bar.endSeconds, bar.steps], [-2, 1.5, 3]);
});

test('createLeadInHold_PausedPartWay_KeepsWhatIsLeftAndStartsOnlyOnce', () => {
    const screen = loadScreen();
    let now = 0;
    let elapsed = 0;
    const hold = screen.createLeadInHold(() => elapsed++, () => now);

    hold.arm(3);
    assert.equal(hold.start(), true);
    now = 1000;
    hold.pause();
    now = 9000;

    assert.equal(hold.paused, true);
    assert.equal(hold.remaining, 2, 'time paused does not count');
    hold.resume();
    hold.arm(5);
    assert.equal(hold.start(), false, 'a running hold does not start again, even re-armed');
    hold.cancel();
    assert.deepEqual([hold.active, hold.remaining, elapsed], [false, 0, 0]);
});
