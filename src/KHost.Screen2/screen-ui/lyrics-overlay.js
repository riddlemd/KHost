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

    function drawLine(page, line, t) {
        const box = line.position;
        const h = ((box && box.height) || 48) * scale;
        const boxWidth = ((box && box.width) || 520) * scale;
        const baseline = offsetY + ((box && box.y) || 40) * scale + h * 0.8;
        let penX = offsetX + ((box && box.x) || 40) * scale;

        ctx2d.textBaseline = 'alphabetic';

        // Height alone decides the size a renderer draws this at, because the box was authored
        // against the font that renderer shapes with. This draws in whatever sans-serif the web
        // view has, which is wider, so a line that just fits there can run off the screen here —
        // and whether it shows depends on the window's shape, which makes it look intermittent.
        // The box is the width the kit promised the line would keep, so hold it to that.
        let fontSize = h * 0.82;
        ctx2d.font = `600 ${fontSize.toFixed(2)}px sans-serif`;

        let measured = 0;
        for (const syl of line.syllables || []) if (syl.text) measured += ctx2d.measureText(syl.text).width;

        if (measured > boxWidth && boxWidth > 0) {
            fontSize *= boxWidth / measured;
            ctx2d.font = `600 ${fontSize.toFixed(2)}px sans-serif`;
        }

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

        for (const page of visiblePages(t)) {
            for (const line of page.lines || []) drawLine(page, line, t);
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
