'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadScreen, stubCanvas } = require('./load-screen.js');

/// A frame queue the test runs by hand; ids start at 1, since the overlay reads 0 as "no frame".
function fakeFrames(screen) {
    const pending = new Map();
    let nextId = 1;
    screen.requestAnimationFrame = (callback) => { pending.set(nextId, callback); return nextId++; };
    screen.cancelAnimationFrame = (id) => { pending.delete(id); };
    return {
        get pending() { return pending.size; },
        runOne() {
            const [id, callback] = pending.entries().next().value;
            pending.delete(id);
            callback(0);
        },
    };
}

const words = {
    pages: [{
        showFromSeconds: 0,
        showUntilSeconds: 10,
        lines: [{ syllables: [{ text: 'la', startSeconds: 0, endSeconds: 1 }] }],
    }],
};

test('createLyricsOverlay_LyricsCleared_SchedulesNoMoreFrames', () => {
    const screen = loadScreen();
    const frames = fakeFrames(screen);
    const canvas = stubCanvas();
    const overlay = screen.createLyricsOverlay(canvas, () => 0.5);

    overlay.setLyrics(words);
    frames.runOne();
    frames.runOne();
    assert.equal(frames.pending, 1, 'a held song keeps one frame queued');

    overlay.setLyrics(null);

    assert.equal(frames.pending, 0);
    assert.equal(canvas.hidden, true);
});
