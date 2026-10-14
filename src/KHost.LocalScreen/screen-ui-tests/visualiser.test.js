'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadScreen } = require('./load-screen.js');

/// A levels track laid out as SongLevels.cs writes it: 'KHLV', version, fps, bands, channels,
/// frame count (uint32 LE), then one byte per band per channel per frame.
function levelsTrack({ fps = 30, bands = 8, channels = 2, frames = 2, fill = 255, version = 1 } = {}) {
    const bytes = new Uint8Array(12 + frames * bands * channels).fill(fill);
    bytes.set([75, 72, 76, 86, version, fps, bands, channels]);
    new DataView(bytes.buffer).setUint32(8, frames, true);
    return bytes.buffer;
}

function samples() {
    return { timeByteArray: new Uint8Array(1024), timeByteArrayL: new Uint8Array(1024), timeByteArrayR: new Uint8Array(1024) };
}

test('parseVisualiserLevels_WellFormedOrNot_ReadsOnlyTheHostsLayout', () => {
    const screen = loadScreen();

    const track = screen.parseVisualiserLevels(levelsTrack({ frames: 3 }));
    assert.deepEqual([track.fps, track.bands, track.channels, track.frames, track.data.length], [30, 8, 2, 3, 48]);

    assert.equal(screen.parseVisualiserLevels(levelsTrack({ bands: 7 })), null, 'band count other than the host splits');
    assert.equal(screen.parseVisualiserLevels(levelsTrack({ version: 2 })), null, 'unknown version');
    assert.equal(screen.parseVisualiserLevels(levelsTrack().slice(0, 20)), null, 'truncated');
});

test('synthesiseVisualiserLevels_DuringLeadInHold_IsSilenceAndInSongIsNot', () => {
    const screen = loadScreen();
    const track = screen.parseVisualiserLevels(levelsTrack());

    const held = samples();
    assert.equal(screen.synthesiseVisualiserLevels(track, -0.5, held), false);
    assert.ok(held.timeByteArray.every((v) => v === 128) && held.timeByteArrayL.every((v) => v === 128));

    const playing = samples();
    assert.equal(screen.synthesiseVisualiserLevels(track, 0.01, playing), true);
    assert.ok(playing.timeByteArrayL.some((v) => v !== 128), 'a loud frame has a waveform');
});
