// Plays a provider's container on the screen instead of a stream of it: the stems are decoded
// here and the lyrics are drawn here, so nothing is transcoded and nothing is waited for.
//
// This is a second path, not a replacement. CDG+mp3 and mp4 have no native path and still arrive
// as HLS, so the engine deliberately wears the shape of the video element it stands in for —
// currentTime, duration, paused, readyState, volume, play/pause — and player.js routes to whichever
// is holding the song.
//
// Fidelity note, because it will matter to whoever compares this against the rendered mp4: the
// host's renderer shapes with HarfBuzz per word and draws positioned glyph runs, recovering each
// syllable from cluster indices. Canvas2D has no positioned-glyph-run primitive, so this measures
// and lays out per syllable with fillText. Kerning across a syllable boundary is therefore the
// browser's rather than HarfBuzz's, and a script needing joins or reordering will not match.
// Matching it exactly needs HarfBuzz in WASM drawing through Path2D.

const KIT_STATE = { readyNothing: 0, readyEnough: 4 };

/// Where a page is on screen, by its own fade times or by what its syllables imply.
/// Mirrors KitRenderer.PageWindow: a page carrying neither gets a window no time falls inside.
function kitPageWindow(page) {
    const show0 = page.fadeInStartSec || 0;
    const show1 = (page.fadeOutStartSec || 0) + (page.fadeOutDurationSec || 0);
    if (show1 > show0) return [show0, show1];

    let first = Infinity, last = -Infinity;
    for (const line of page.lines || []) {
        for (const word of line.words || []) {
            for (const syl of word.syllables || []) {
                if (syl.startSec < first) first = syl.startSec;
                if (syl.endSec > last) last = syl.endSec;
            }
        }
    }

    return first > last ? [1, -1] : [first - 0.5, last + 1.0];
}

function createKitEngine(canvas, onError) {
    const ctx2d = canvas.getContext('2d');
    let audio = null;          // AudioContext, made on load so a page that never plays makes none
    let master = null;
    let stems = [];            // { role, buffer, gain, source }
    let timing = null;
    let scale = 1;
    let offsetX = 0;
    let offsetY = 0;

    let startedAtCtx = 0;      // audio clock reading when the current run began
    let startedFrom = 0;       // song position that run began at
    let paused = true;
    let pausedAt = 0;
    let duration = 0;
    let frame = 0;
    let volume = 1;
    let mix = { lead: 100, backing: 100 };

    const fail = (e) => { try { onError(String(e && e.message ? e.message : e)); } catch { /* */ } };

    /// The engine's clock. Position comes from the audio device, not from a timer, because the
    /// sound is what the room hears — a wall clock would drift away from it and take the words with it.
    function currentTime() {
        if (!audio) return 0;
        if (paused) return pausedAt;

        return startedFrom + (audio.currentTime - startedAtCtx);
    }

    function stemGain(role) {
        // Music is the bed and always at full; the two voice stems are what a host balances.
        if (role === 'Lead Vocal') return Math.max(0, Math.min(100, mix.lead)) / 100;
        if (role === 'Backing Vocal') return Math.max(0, Math.min(100, mix.backing)) / 100;
        return 1;
    }

    function applyMix() {
        for (const s of stems) {
            if (s.gain) s.gain.gain.value = stemGain(s.role);
        }
    }

    function stopSources() {
        for (const s of stems) {
            if (!s.source) continue;
            try { s.source.onended = null; s.source.stop(); } catch { /* already finished */ }
            s.source = null;
        }
    }

    /// Every stem starts on one scheduled instant, which is what keeps them in phase with each
    /// other. Starting them "now" one after another would stagger them by however long the loop took.
    function startSources(fromSec) {
        stopSources();
        if (!audio) return;

        const when = audio.currentTime + 0.05;
        for (const s of stems) {
            const source = audio.createBufferSource();
            source.buffer = s.buffer;
            source.connect(s.gain);
            source.start(when, Math.max(0, Math.min(fromSec, s.buffer.duration)));
            s.source = source;
        }

        startedAtCtx = when;
        startedFrom = fromSec;
    }

    function visiblePages(t) {
        const out = [];
        for (const page of timing.pages || []) {
            if (t >= page._show0 && t <= page._show1) out.push(page);
        }
        return out;
    }

    function drawLine(page, line, t) {
        const h = (line.height || 48) * scale;
        const fontSize = h * 0.82;
        const baseline = offsetY + (line.y || 40) * scale + h * 0.8;
        let penX = offsetX + (line.x || 40) * scale;

        ctx2d.font = `600 ${Math.round(fontSize)}px sans-serif`;
        ctx2d.textBaseline = 'alphabetic';

        // Between words, not inside them: a syllable's text carries no trailing space, so without
        // this a line reads "Mama,lifehadjustbegun".
        const spaceWidth = ctx2d.measureText(' ').width;
        let firstWord = true;

        for (const word of line.words || []) {
            if (!firstWord) penX += spaceWidth;
            firstWord = false;

            for (const syl of word.syllables || []) {
                if (!syl.text) continue;

                const w = ctx2d.measureText(syl.text).width;

                // The outline is what keeps the words legible over whatever is behind them.
                ctx2d.lineWidth = Math.max(2, fontSize * 0.09);
                ctx2d.lineJoin = 'round';
                ctx2d.strokeStyle = 'rgba(0,0,0,0.85)';
                ctx2d.strokeText(syl.text, penX, baseline);

                ctx2d.fillStyle = page.inactiveColor || '#ffffff';
                ctx2d.fillText(syl.text, penX, baseline);

                // Linear, no easing — the same ramp the rendered version uses.
                const span = syl.endSec - syl.startSec;
                const frac = span <= 0
                    ? (t >= syl.startSec ? 1 : 0)
                    : Math.max(0, Math.min(1, (t - syl.startSec) / span));

                if (frac > 0) {
                    ctx2d.save();
                    ctx2d.beginPath();
                    // Clipped reveal over text already drawn, rather than a per-glyph colour swap:
                    // the wipe has to be able to stop part way through a letter.
                    ctx2d.rect(page.rtl ? penX + w * (1 - frac) : penX, baseline - h,
                        w * frac, h * 2);
                    ctx2d.clip();
                    ctx2d.fillStyle = page.activeColor || '#8558fa';
                    ctx2d.fillText(syl.text, penX, baseline);
                    ctx2d.restore();
                }

                penX += w;
            }
        }
    }

    function draw() {
        frame = requestAnimationFrame(draw);
        if (!timing) return;

        sizeCanvas();

        const t = currentTime();
        ctx2d.clearRect(0, 0, canvas.width, canvas.height);

        for (const page of visiblePages(t)) {
            for (const line of page.lines || []) drawLine(page, line, t);
        }
    }

    /// Matches the backing store to the space the canvas actually occupies.
    /// <remarks>Measured, never assumed, and re-checked every frame: a hidden canvas measures zero,
    /// so sizing one before it is shown leaves a 1x1 buffer that draws a whole song into a single
    /// pixel — the clock runs, the audio plays, and the screen stays black.</remarks>
    function sizeCanvas() {
        // Drawn at the device's own pixels: the lyrics are the picture here, and a canvas scaled
        // up afterwards would show it soft where the rendered version was sharp.
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
        const height = Math.max(1, Math.round(canvas.clientHeight * dpr));

        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }

        // Fit the whole logical page, not just its height. The renderer sizes its output to the
        // kit's own aspect so height alone is enough there; a screen is whatever shape the room
        // bought, and scaling by height on a narrow one runs the words off the side.
        const logicalH = (timing && timing.logicalHeight) || 360;
        const logicalW = (timing && timing.logicalWidth) || (logicalH * 16 / 9);
        scale = Math.min(canvas.width / logicalW, canvas.height / logicalH);

        // Centred in whatever is left over, so the words sit in the middle of the picture rather
        // than hard against the top left corner.
        offsetX = (canvas.width - logicalW * scale) / 2;
        offsetY = (canvas.height - logicalH * scale) / 2;
    }

    window.addEventListener('resize', () => { if (timing) sizeCanvas(); });

    return {
        get isActive() { return timing !== null; },
        get currentTime() { return currentTime(); },
        set currentTime(v) { this.seek(v); },
        get duration() { return duration; },
        get paused() { return paused; },
        get ended() { return duration > 0 && currentTime() >= duration; },
        get readyState() { return timing ? KIT_STATE.readyEnough : KIT_STATE.readyNothing; },
        get playbackRate() { return 1; },

        get volume() { return volume; },
        set volume(v) {
            volume = Math.max(0, Math.min(1, v));
            if (master) master.gain.value = volume;
        },

        setMix(lead, backing) {
            mix = { lead: Number.isFinite(lead) ? lead : 100, backing: Number.isFinite(backing) ? backing : 100 };
            applyMix();
        },

        /// `kit` is { logicalHeight, durationSec, pages, stems:[{role,url}] }. The stems are URLs
        /// rather than bytes so the web view streams and decodes them itself.
        async load(kit) {
            this.teardown();

            audio = new (window.AudioContext || window.webkitAudioContext)();
            master = audio.createGain();
            master.gain.value = volume;
            master.connect(audio.destination);

            const decoded = await Promise.all((kit.stems || []).map(async (s) => {
                const response = await fetch(s.url);
                if (!response.ok) throw new Error(`stem ${s.role}: ${response.status}`);
                const buffer = await audio.decodeAudioData(await response.arrayBuffer());
                const gain = audio.createGain();
                gain.connect(master);
                return { role: s.role, buffer, gain, source: null };
            }));

            stems = decoded;
            applyMix();

            timing = kit;
            // Worked out once: a page's window cannot change, and asking per frame would walk every
            // line and word of every page sixty times a second to answer the same thing.
            for (const page of timing.pages || []) {
                const [a, b] = kitPageWindow(page);
                page._show0 = a;
                page._show1 = b;
            }

            duration = kit.durationSec
                || stems.reduce((m, s) => Math.max(m, s.buffer.duration), 0);

            paused = true;
            pausedAt = 0;
            // Shown before measuring: a hidden element has no size to measure.
            canvas.hidden = false;
            sizeCanvas();
            if (!frame) frame = requestAnimationFrame(draw);
        },

        async play() {
            if (!audio || !timing) return;
            if (audio.state === 'suspended') await audio.resume();
            if (!paused) return;

            paused = false;
            startSources(pausedAt);
        },

        pause() {
            if (!audio || paused) return;
            pausedAt = currentTime();
            paused = true;
            stopSources();
        },

        seek(position) {
            if (!audio || !timing) return;
            const target = Math.max(0, Math.min(position || 0, duration));

            if (paused) { pausedAt = target; return; }
            startSources(target);
        },

        /// Ramps the master gain rather than an element's opacity: the engine's picture is the
        /// words, and fading those out would leave the room hearing a song it cannot read.
        async fadeOutAndStop(fadeMs) {
            if (!audio || !master) { this.teardown(); return; }

            const ms = Math.max(1, fadeMs || 0);
            const endsAt = audio.currentTime + ms / 1000;
            master.gain.cancelScheduledValues(audio.currentTime);
            master.gain.setValueAtTime(master.gain.value, audio.currentTime);
            master.gain.linearRampToValueAtTime(0.0001, endsAt);

            await new Promise((resolve) => setTimeout(resolve, ms));
            this.teardown();
        },

        teardown() {
            stopSources();
            stems = [];
            timing = null;
            duration = 0;
            paused = true;
            pausedAt = 0;
            canvas.hidden = true;
            if (frame) { cancelAnimationFrame(frame); frame = 0; }
            if (ctx2d) ctx2d.clearRect(0, 0, canvas.width, canvas.height);
            if (audio) { const a = audio; audio = null; master = null; a.close().catch(() => { /* already gone */ }); }
        },

        fail,
    };
}
