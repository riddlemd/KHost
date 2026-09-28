// The Visualisations page's preview: the screen's own visualiser and words overlay (copied from
// screen-ui by copy:vendors), run in a sandboxed frame that the page posts each entry to. Sandboxed
// because a preset's equations are code, and an imported one is whatever file the host picked:
// here it runs with no origin, away from the console's session.

/// How long the made-up song runs before it loops, and its tempo.
const PREVIEW_SECONDS = 16;
const PREVIEW_BPM = 120;

/// A levels track, in parseVisualiserLevels' shape, for a made-up song: a kick on every beat, a
/// snare on two and four, hats on the off-beats and a swell over four bars. Real swings, so a
/// sensitivity change shows as more or less punch rather than as nothing.
function buildPreviewLevels(seconds, fps) {
    const bands = VISUALISER_BAND_EDGES.length - 1;
    const channels = 2;
    const frames = Math.round(seconds * fps);
    const data = new Uint8Array(frames * bands * channels);
    const beat = 60 / PREVIEW_BPM;

    for (let f = 0; f < frames; f++) {
        const t = f / fps;
        const phase = (t % beat) / beat;
        const kick = Math.exp(-phase * 8);
        const snare = Math.floor(t / beat) % 2 === 1 ? Math.exp(-phase * 10) : 0;
        const hat = Math.exp(-(((t + beat / 2) % beat) / beat) * 20);
        const swell = 0.5 + 0.5 * Math.sin((2 * Math.PI * t) / (beat * 16));
        const levels = [
            0.1 + kick * 0.9, 0.15 + kick * 0.7, 0.2 + swell * 0.5, 0.25 + swell * 0.4,
            0.2 + snare * 0.7, 0.2 + snare * 0.5, 0.15 + hat * 0.7, 0.1 + hat * 0.8,
        ];

        for (let channel = 0; channel < channels; channel++) {
            const row = (f * channels + channel) * bands;
            // A quarter decibel a byte under the band's loudest, as the host stores them.
            levels.forEach((level, band) => { data[row + band] = Math.max(1, Math.min(255, Math.round(255 + 80 * Math.log10(level)))); });
        }
    }

    return { fps, bands, channels, frames, data };
}

/// Two lines across the middle of the picture, where a band shows whether it is behind each line
/// rather than along the bottom, sung through once a loop.
function buildPreviewLyrics() {
    const words = (text, from, to) => text.split(' ').map((word, i, all) => {
        const step = (to - from) / all.length;
        return { startSeconds: from + i * step, endSeconds: from + (i + 1) * step, text: (i > 0 ? ' ' : '') + word };
    });

    return {
        bounds: { width: 640, height: 360 },
        countIns: [],
        pages: [{
            showFromSeconds: 0,
            showUntilSeconds: PREVIEW_SECONDS,
            lines: [
                { position: { x: 80, y: 130, width: 480, height: 44 }, syllables: words('Sing it like nobody is listening', 1, 7) },
                { position: { x: 80, y: 186, width: 480, height: 44 }, syllables: words('and the whole room joins in', 8, 14) },
            ],
        }],
    };
}

(function startPreview() {
    const clock = () => (performance.now() / 1000) % PREVIEW_SECONDS;
    const report = (message) => console.warn(message);

    const visualiser = createVisualiser(document.getElementById('visualiser'), {
        eqCanvas: document.getElementById('visualiser-eq'),
        engine: window.butterchurn && window.butterchurn.default,
        presets: VISUALISER_PRESETS,
        reportError: report,
        clock,
    });
    visualiser.setLevels(buildPreviewLevels(PREVIEW_SECONDS, 30));

    const overlay = createLyricsOverlay(document.getElementById('lyrics'), clock);
    overlay.setLyrics(buildPreviewLyrics());

    window.addEventListener('resize', () => visualiser.resize());

    window.addEventListener('message', (event) => {
        const message = event.data;
        if (event.source !== window.parent || !message || message.type !== 'visualisation') return;

        overlay.setDarkenBands(message.darken === true);
        visualiser.setLook(message);
        visualiser.show(message);
    });

    // Asked for the entry again once listening: the page may have posted before this ran.
    window.parent.postMessage({ type: 'visualiser-preview-ready' }, '*');
})();
