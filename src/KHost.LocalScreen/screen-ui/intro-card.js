// Names the song and its singer from the song's start until its first page of words arrives.
//
// Only for a song whose words this screen draws itself: the host sends a card only beside a
// timing, since a picture that carries its own words carries its own intro. It reads the same
// clock the words do, so a seek past the first page takes it down and a seek back puts it up.

/// Seconds the card takes to go, ending as the first page shows: the page's lines may land where
/// the card was, and words drawn through a card read as neither.
const INTRO_FADE_SECONDS = 0.6;

/// How opaque the card is at song position t, given when the first page shows.
function introCardOpacity(t, firstPageAt) {
    if (t === null || t === undefined || firstPageAt === null) return 0;
    return Math.max(0, Math.min(1, (firstPageAt - t) / INTRO_FADE_SECONDS));
}

/// The band of the lyric page the card may use, in the timing's own units: the whole page, or the
/// part above a count-in bar that runs while the card is up, which the room is reading as its cue.
function introCardBand(lyrics, firstPageAt) {
    const bounds = (lyrics && lyrics.bounds) || null;
    const height = (bounds && bounds.height) || 360;
    const gap = height * 0.03;
    let top = 0;
    let bottom = height;

    for (const countIn of (lyrics && lyrics.countIns) || []) {
        const box = countIn.position;
        if (!box || countIn.startSeconds >= firstPageAt || countIn.endSeconds <= 0) continue;

        bottom = Math.min(bottom, box.y - gap);
    }

    // A bar near the top leaves too little above it to read a title in, so the card goes under it.
    if (bottom - top < height * 0.3) {
        top = 0;
        bottom = height;
        for (const countIn of (lyrics && lyrics.countIns) || []) {
            const box = countIn.position;
            if (!box || countIn.startSeconds >= firstPageAt || countIn.endSeconds <= 0) continue;

            top = Math.max(top, box.y + box.height + gap);
        }
    }

    return { top, bottom };
}

/// `clock` answers the song position in seconds, or null when nothing is holding the song.
function createIntroCard(layer, clock) {
    const card = document.createElement('div');
    card.className = 'kh-intro';
    card.hidden = true;
    layer.appendChild(card);

    let lyrics = null;
    let firstPageAt = null;
    let frame = 0;

    function line(className, text) {
        const el = document.createElement('span');
        el.className = className;
        el.textContent = text;
        return el;
    }

    /// Fits the card into the band over the page, the page fitted exactly as the words are fitted,
    /// so the card lands above the bar however the screen is shaped.
    function layout() {
        const width = layer.clientWidth || 0;
        const height = layer.clientHeight || 0;
        const bounds = (lyrics && lyrics.bounds) || null;
        const logicalW = (bounds && bounds.width) || 640;
        const logicalH = (bounds && bounds.height) || 360;
        const scale = Math.min(width / logicalW, height / logicalH);
        const offsetX = (width - logicalW * scale) / 2;
        const offsetY = (height - logicalH * scale) / 2;
        const band = introCardBand(lyrics, firstPageAt);
        const bandHeight = (band.bottom - band.top) * scale;

        card.style.left = `${offsetX}px`;
        card.style.width = `${logicalW * scale}px`;
        card.style.top = `${offsetY + band.top * scale}px`;
        card.style.height = `${bandHeight}px`;

        // Sized off whichever is tighter, the page's width or the band's height, so a long title
        // on a short band shrinks rather than running into the bar.
        const unit = Math.min(logicalW * scale / 100, bandHeight / 36);
        card.style.setProperty('--kh-intro-unit', `${Math.max(0, unit)}px`);
    }

    function tick() {
        const opacity = introCardOpacity(firstPageAt === null ? null : clock(), firstPageAt);

        card.hidden = opacity <= 0;
        card.style.opacity = String(opacity);

        if (!card.hidden) layout();
    }

    function loop() {
        tick();
        frame = firstPageAt === null ? 0 : requestAnimationFrame(loop);
    }

    return {
        /// Whether the card is on screen at this moment.
        get isShowing() { return !card.hidden; },

        /// Moves the card to the clock now rather than on the next frame.
        tick,

        /// `intro` is the host's card and `timing` the words it heads; either null clears the card.
        set(intro, timing) {
            const pages = (timing && timing.pages) || [];
            card.replaceChildren();

            if (!intro || !intro.title || pages.length === 0) {
                lyrics = null;
                firstPageAt = null;
                tick();
                return;
            }

            lyrics = timing;
            firstPageAt = Math.min(...pages.map((page) => page.showFromSeconds));

            // Nodes, not markup: a singer types their own name.
            card.appendChild(line('kh-intro__title', intro.title));
            if (intro.artist) card.appendChild(line('kh-intro__artist', intro.artist));
            if (intro.singer) {
                const singer = line('kh-intro__singer', '');
                singer.appendChild(line('kh-intro__label', 'Sung by'));
                singer.appendChild(document.createTextNode(` ${intro.singer}`));
                card.appendChild(singer);
            }

            tick();
            if (!frame) frame = requestAnimationFrame(loop);
        },
    };
}
