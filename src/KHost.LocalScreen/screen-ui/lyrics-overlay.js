// Draws a song's words over whatever is playing, from timing the host sent with the load.
//
// The audio is not this file's business. It arrives as an ordinary HLS stream like every other
// song, mixed and pitched and stretched by ffmpeg, and the words follow the element playing it —
// so there is one clock in the room and the overlay cannot drift away from the sound.
//
// Fidelity note, because it will matter to whoever compares this against a rendered mp4: a
// renderer shapes with HarfBuzz per word and draws positioned glyph runs, recovering each syllable
// from cluster indices. Canvas2D has no positioned-glyph-run primitive, so this measures and lays
// out per syllable with fillText. Kerning across a syllable boundary is therefore the browser's
// rather than HarfBuzz's, and a script needing joins or reordering will not match.

/// `clock` answers the song position in seconds, or null when nothing is playing.
function createLyricsOverlay(canvas, clock) {
    const ctx2d = canvas.getContext('2d');
    let lyrics = null;
    let scale = 1;
    let offsetX = 0;
    let offsetY = 0;
    let frame = 0;

    // How long a count-in takes to clear once a page arrives inside its window. A timing brings
    // the next page up a beat before the gap ends, often over the bar's own spot.
    const HANDOVER_SECONDS = 0.5;

    function css(color, fallback) {
        return color ? `rgb(${color.r},${color.g},${color.b})` : fallback;
    }

    function visiblePages(t) {
        const out = [];
        for (const page of lyrics.pages || []) {
            if (t >= page.showFromSeconds && t <= page.showUntilSeconds) out.push(page);
        }
        return out;
    }

    /// Linear, clamped to 0..1: where t sits between two song positions.
    function progress(t, from, to) {
        return to <= from ? (t >= from ? 1 : 0) : Math.max(0, Math.min(1, (t - from) / (to - from)));
    }

    function roundedRect(x, y, w, h, r) {
        ctx2d.beginPath();
        ctx2d.moveTo(x + r, y);
        ctx2d.arcTo(x + w, y, x + w, y + h, r);
        ctx2d.arcTo(x + w, y + h, x, y + h, r);
        ctx2d.arcTo(x, y + h, x, y, r);
        ctx2d.arcTo(x, y, x + w, y, r);
        ctx2d.closePath();
    }

    /// When the first page to arrive inside a count-in's window shows, or null when none does.
    function handoverAt(countIn) {
        let at = null;
        for (const page of lyrics.pages || []) {
            const from = page.showFromSeconds;
            if (from > countIn.startSeconds && from < countIn.endSeconds && (at === null || from < at)) at = from;
        }
        return at;
    }

    /// The bar across a gap: filled left to right over its whole window, eased in and out over one
    /// step, with the last `steps` steps counted down over it as n, n-1 … 1. It gives way to the
    /// next page as that page arrives, whose lead-in is the cue from then on.
    function drawCountIn(countIn, t) {
        const box = countIn.position;
        if (!box || t < countIn.startSeconds || t >= countIn.endSeconds) return;

        const handover = handoverAt(countIn);
        // Gone by the time the page shows, not after: a singer pre-reads the first line as it lands.
        const leaving = handover === null ? 1 : 1 - progress(t, handover - HANDOVER_SECONDS, handover);
        if (leaving <= 0) return;

        const step = countIn.stepSeconds || 0;
        const alpha = Math.min(leaving, step > 0
            ? Math.min(1, (t - countIn.startSeconds) / step, (countIn.endSeconds - t) / step)
            : 1);
        const x = offsetX + box.x * scale;
        const y = offsetY + box.y * scale;
        const w = box.width * scale;
        const h = box.height * scale;
        const fill = progress(t, countIn.startSeconds, countIn.endSeconds);

        ctx2d.save();
        ctx2d.globalAlpha = alpha;
        roundedRect(x, y, w, h, 4 * scale);
        ctx2d.fillStyle = css(countIn.inactive, '#ffffff');
        ctx2d.fill();

        ctx2d.save();
        ctx2d.clip();
        ctx2d.fillStyle = css(countIn.active, '#8558fa');
        ctx2d.fillRect(x, y, w * fill, h);
        ctx2d.restore();

        if (countIn.borderWidth > 0) {
            ctx2d.lineWidth = countIn.borderWidth * scale;
            ctx2d.strokeStyle = css(countIn.border, '#000000');
            ctx2d.stroke();
        }
        ctx2d.restore();

        // The countdown is not eased with the bar: the last number is the one that must be read.
        // It still leaves with the handover, or its digits land on the page's first line.
        const steps = countIn.steps || 0;
        if (step <= 0 || steps <= 0 || t < countIn.endSeconds - steps * step) return;

        const n = Math.min(steps, Math.floor((countIn.endSeconds - t) / step) + 1);
        const fontSize = h * 1.6;
        ctx2d.save();
        ctx2d.globalAlpha = leaving;
        ctx2d.font = `800 ${fontSize.toFixed(2)}px sans-serif`;
        ctx2d.textAlign = 'center';
        ctx2d.textBaseline = 'alphabetic';
        ctx2d.lineWidth = Math.max(2, fontSize * 0.06);
        ctx2d.lineJoin = 'round';
        ctx2d.strokeStyle = 'rgba(0,0,0,0.85)';
        ctx2d.strokeText(String(n), x + w / 2, y + h * 1.25);
        ctx2d.fillStyle = '#ffffff';
        ctx2d.fillText(String(n), x + w / 2, y + h * 1.25);
        ctx2d.restore();
    }

    /// A small block that travels in to the line's leading edge, arriving as its first syllable lights.
    function drawLeadIn(page, line, t, baseline, fontSize) {
        const leadIn = line.leadIn;
        const box = line.position;
        const first = (line.syllables || [])[0];
        if (!leadIn || !box || !first || t < leadIn.startSeconds || t >= first.startSeconds) return;

        const run = box.x - leadIn.x;
        // Mirrored for right to left: the same run, made into the right edge from outside it.
        const from = lyrics.isRightToLeft ? box.x + box.width + run : leadIn.x;
        const to = lyrics.isRightToLeft ? box.x + box.width : box.x;
        const head = from + (to - from) * progress(t, leadIn.startSeconds, first.startSeconds);

        const w = 10 * scale;
        const h = box.height * 0.3 * scale;
        const x = offsetX + head * scale - w / 2;
        const y = baseline - fontSize * 0.35 - h / 2;

        ctx2d.fillStyle = css(page.active, '#8558fa');
        ctx2d.fillRect(x, y, w, h);
        ctx2d.lineWidth = 3 * scale;
        ctx2d.strokeStyle = 'rgba(0,0,0,0.85)';
        ctx2d.strokeRect(x, y, w, h);
    }

    /// Where each line of a page sits: its own position, or stacked directly under the line before
    /// it, the first at the default spot. The host paints burned-in words by the same rule.
    function lineBoxes(lines) {
        const boxes = [];
        for (const line of lines) {
            const above = boxes[boxes.length - 1];
            boxes.push(line.position || { x: 40, y: above ? above.y + above.height : 40, width: 520, height: 48 });
        }
        return boxes;
    }

    function drawLine(page, line, box, t) {
        const h = box.height * scale;
        const boxWidth = box.width * scale;
        const baseline = offsetY + box.y * scale + h * 0.8;
        let penX = offsetX + box.x * scale;

        ctx2d.textBaseline = 'alphabetic';

        // Height alone decides the size a renderer draws this at, because the box was authored
        // against the font that renderer shapes with. This draws in whatever sans-serif the web
        // view has, which is wider, so a line that just fits there can run off the screen here —
        // and whether it shows depends on the window's shape, which makes it look intermittent.
        // The box is the width the format promised the line would keep, so hold it to that.
        let fontSize = h * 0.82;
        ctx2d.font = `600 ${fontSize.toFixed(2)}px sans-serif`;

        let measured = 0;
        for (const syl of line.syllables || []) if (syl.text) measured += ctx2d.measureText(syl.text).width;

        if (measured > boxWidth && boxWidth > 0) {
            fontSize *= boxWidth / measured;
            ctx2d.font = `600 ${fontSize.toFixed(2)}px sans-serif`;
        }

        drawLeadIn(page, line, t, baseline, fontSize);

        for (const syl of line.syllables || []) {
            if (!syl.text) continue;

            // Spacing is the provider's: a syllable is part of a word as often as it is a whole
            // one, so a space put in here would land inside every word that was split.
            const w = ctx2d.measureText(syl.text).width;

            // The outline is what keeps the words legible over whatever is behind them.
            ctx2d.lineWidth = Math.max(2, fontSize * 0.09);
            ctx2d.lineJoin = 'round';
            ctx2d.strokeStyle = 'rgba(0,0,0,0.85)';
            ctx2d.strokeText(syl.text, penX, baseline);

            ctx2d.fillStyle = css(page.inactive, '#ffffff');
            ctx2d.fillText(syl.text, penX, baseline);

            // Linear, no easing.
            const span = syl.endSeconds - syl.startSeconds;
            const frac = span <= 0
                ? (t >= syl.startSeconds ? 1 : 0)
                : Math.max(0, Math.min(1, (t - syl.startSeconds) / span));

            if (frac > 0) {
                ctx2d.save();
                ctx2d.beginPath();
                // Clipped reveal over text already drawn, rather than a per-glyph colour swap:
                // the wipe has to be able to stop part way through a letter.
                ctx2d.rect(lyrics.isRightToLeft ? penX + w * (1 - frac) : penX, baseline - h,
                    w * frac, h * 2);
                ctx2d.clip();
                ctx2d.fillStyle = css(page.active, '#8558fa');
                ctx2d.fillText(syl.text, penX, baseline);
                ctx2d.restore();
            }

            penX += w;
        }
    }

    function draw() {
        frame = requestAnimationFrame(draw);
        if (!lyrics) return;

        sizeCanvas();
        ctx2d.clearRect(0, 0, canvas.width, canvas.height);

        // Null means nothing is holding the song — between numbers, or before the stream opens.
        // Drawing at zero then would light the first page over whatever is on screen.
        const t = clock();
        if (t === null || t === undefined) return;

        for (const countIn of lyrics.countIns || []) drawCountIn(countIn, t);

        for (const page of visiblePages(t)) {
            const lines = page.lines || [];
            const boxes = lineBoxes(lines);
            lines.forEach((line, i) => drawLine(page, line, boxes[i], t));
        }
    }

    /// Matches the backing store to the space the canvas actually occupies.
    /// <remarks>Measured, never assumed, and re-checked every frame: a hidden canvas measures zero,
    /// so sizing one before it is shown leaves a 1x1 buffer that draws a whole song into a single
    /// pixel.</remarks>
    function sizeCanvas() {
        // Drawn at the device's own pixels: a canvas scaled up afterwards shows the words soft.
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
        const height = Math.max(1, Math.round(canvas.clientHeight * dpr));

        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }

        // Fit the whole logical page, not just its height: a screen is whatever shape the room
        // bought, and scaling by height on a narrow one runs the words off the side.
        const bounds = (lyrics && lyrics.bounds) || null;
        const logicalW = (bounds && bounds.width) || 640;
        const logicalH = (bounds && bounds.height) || 360;
        scale = Math.min(canvas.width / logicalW, canvas.height / logicalH);

        // Centred in whatever is left over, so the words sit in the middle of the picture.
        offsetX = (canvas.width - logicalW * scale) / 2;
        offsetY = (canvas.height - logicalH * scale) / 2;
    }

    window.addEventListener('resize', () => { if (lyrics) sizeCanvas(); });

    return {
        get isActive() { return lyrics !== null; },

        /// Wipes what is drawn and keeps the timing: the next frame draws again if a song is held.
        clear() { ctx2d.clearRect(0, 0, canvas.width, canvas.height); },

        /// `value` is the host's TimedLyrics, or null for a song with no words to draw.
        setLyrics(value) {
            lyrics = value && value.pages && value.pages.length > 0 ? value : null;

            // Cleared here rather than left to the loop: with no lyrics the loop returns before it
            // clears, so the last song's final page would stay lit over the next one.
            ctx2d.clearRect(0, 0, canvas.width, canvas.height);
            canvas.hidden = lyrics === null;

            if (lyrics && !frame) frame = requestAnimationFrame(draw);
        },
    };
}
