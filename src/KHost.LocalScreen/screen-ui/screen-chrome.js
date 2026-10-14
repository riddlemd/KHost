// Everything the screen draws around the song rather than of it: the marquee band, the corners (QR
// code and break music card), the next-singer card and the venue's colours. None of it touches the
// transport, so the player hands it the few things it shares and asks it to draw.

// Pixels per second the band travels when a venue has not chosen. A rate, not a lap time, so a
// long line does not race to keep the same pace as a short one.
const MARQUEE_SPEED = 90;
const MARQUEE_SPEED_MIN = 15;
const MARQUEE_SPEED_MAX = 400;

// A ceiling on the tiling. A one-word band on a wide screen would otherwise ask for dozens of
// copies, and past a point the band is full either way. What it buys is nodes, not smoothness.
const MARQUEE_COPIES_MAX = 12;

// The band is fixed to an edge: a size past the ceiling covers the picture rather than sitting on it.
const MARQUEE_FONT_SIZE_MIN_PX = 12;
const MARQUEE_FONT_SIZE_MAX_PX = 96;

// A floor on one lap, so a short line at a high speed does not flicker past.
const MARQUEE_LAP_MIN_SECONDS = 4;

// A drag reports every frame, and re-tiling replaces the track's children, which restarts the scroll.
const MARQUEE_RETILE_SETTLE_MS = 150;

const QR_CORNERS = ['bottomright', 'bottomleft', 'topright', 'topleft'];
const QR_SIZES = ['small', 'medium', 'large', 'extralarge'];

// What the next-singer card may be drawn over, as the host spells it.
const CARD_BACKGROUNDS = ['over', 'blackout', 'visualisation'];

// Kind -> the class that colours it; 'other' and 'separator' are handled separately below.
const MARQUEE_SEGMENT_CLASSES = { singer: 'marquee-singer', song: 'marquee-song' };

function createScreenChrome({
    doc,
    overlay,
    visualiser,
    background,
    loadVisualiserLevels,
    elementAudioSource,
    releaseElementTap,
    setTimer = setTimeout,
    clearTimer = clearTimeout,
    ResizeObserverType = typeof ResizeObserver === 'function' ? ResizeObserver : null,
}) {
    const still = doc.getElementById('still');
    const placeholder = doc.getElementById('placeholder');
    const nextSinger = doc.getElementById('nextSinger');
    const marquee = doc.getElementById('marquee');
    const marqueeTrack = doc.getElementById('marquee-track');
    const marqueeViewport = doc.getElementById('marquee-viewport');
    const corners = new Map(
        [...doc.querySelectorAll('.kh-corner')].map((el) => [el.dataset.corner, el]));

    // What each owner last put in a corner, so a corner can be rebuilt without the other's command.
    // The card sits above the code, which keeps the pair from swapping places as each comes and goes.
    const cornerItems = { breakMusic: null, qr: null };

    // What the band last read, so a resend that changes nothing readable (a colour tweak, the same
    // queue re-announced after an unrelated change) does not yank the scroll back to its start.
    let marqueeSignature = null;

    // Whether the visualiser on screen is the card's rather than a song's.
    let cardVisualiser = false;

    function drawCorners() {
        for (const el of corners.values()) el.replaceChildren();

        for (const key of ['breakMusic', 'qr']) {
            const item = cornerItems[key];
            if (!item) continue;

            const corner = corners.get(item.corner);
            if (corner) corner.appendChild(item.node);
        }
    }

    // The inset belongs to the corner, so the last command carrying one sets it for every corner.
    // Two things stacked against one edge have to agree on how far in it is.
    function setCornerOffset(offset) {
        if (!Number.isFinite(offset) || offset <= 0) return;

        doc.documentElement.style.setProperty('--kh-corner-offset', `${offset}vmin`);
    }

    /// Names who is up, over the whole picture. Nothing here takes it down: the next thing drawn does.
    function showNextSinger(message) {
        nextSinger.querySelector('.kh-next__singer').textContent = message.singer || '';

        const song = nextSinger.querySelector('.kh-next__song');
        // A singer on the list with nothing queued is named alone rather than under an empty line.
        song.textContent = message.artist ? `${message.song} - ${message.artist}` : (message.song || '');
        song.hidden = !message.song;

        const background = CARD_BACKGROUNDS.includes(message.background) ? message.background : 'over';
        // Another announcement over this one may ask for something else behind it.
        if (background !== 'visualisation') stopCardVisualiser();

        nextSinger.dataset.background = background;
        nextSinger.hidden = false;

        if (background === 'over') {
            showIdlePicture();
            return;
        }

        // Black, or a visualisation, replaces the venue's picture, so it goes while the card is up.
        still.hidden = true;
        placeholder.hidden = true;
        if (background === 'visualisation') startCardVisualiser(message.visualiser);
    }

    /// Puts the card's visualisation up, unless a song's is already drawing: the card sits over that
    /// one and leaves it to the song. One that cannot be drawn leaves the card over the venue's picture.
    function startCardVisualiser(card) {
        if (!card || (visualiser.active && !cardVisualiser)) return;

        // The card never plays a video; one sent here draws nothing, leaving the card over the picture.
        const { videoUrl, ...look } = card;

        cardVisualiser = true;
        visualiser.setLook(look);
        // No song to read levels from. It hears the break music where the page can tap it (not in
        // WebKit, which has no captureStream, nor a provider playing outside the page); else it draws
        // its idle motion.
        loadVisualiserLevels(null);
        visualiser.freeze(false);
        visualiser.setAudio(elementAudioSource(background));
        visualiser.show(look).then((up) => {
            if (up || !cardVisualiser || nextSinger.hidden) return;

            cardVisualiser = false;
            nextSinger.dataset.background = 'over';
            showIdlePicture();
        });
    }

    /// Takes down only what the card put up.
    function stopCardVisualiser() {
        if (!cardVisualiser) return;

        cardVisualiser = false;
        visualiser.hide();
        visualiser.setAudio(null);
        releaseElementTap();
    }

    // The next break-music track is a new source, so the card's tap follows it.
    background.addEventListener('playing', () => {
        if (cardVisualiser) visualiser.setAudio(elementAudioSource(background));
    });

    /// Anything that redraws the picture clears the card. Deliberately not every command: a marquee or
    /// a code update is not somebody taking the screen back, and would cancel an announcement the host
    /// had only just made.
    function clearNextSinger() {
        if (nextSinger.hidden) return;

        nextSinger.hidden = true;
        stopCardVisualiser();
        showIdlePicture();
    }

    /// The venue's still if it set one, else the placeholder. Asked of the still's source, not its
    /// visibility: the card hid the still, and the host does not resend a picture it never took down.
    function showIdlePicture() {
        still.hidden = !still.getAttribute('src');
        placeholder.hidden = !still.hidden;
    }

    // What is playing between singers. Text only: a title and an artist off a provider, which is
    // exactly why it is built as nodes rather than markup.
    function setBreakMusicCard(message) {
        if (!message.enabled || !message.title) {
            cornerItems.breakMusic = null;
            drawCorners();
            return;
        }

        setCornerOffset(message.offset);

        const card = doc.createElement('div');
        card.className = 'kh-break-music';

        const title = doc.createElement('span');
        title.className = 'kh-break-music__title';
        title.textContent = message.title;
        card.appendChild(title);

        if (message.artist) {
            const artist = doc.createElement('span');
            artist.className = 'kh-break-music__artist';
            artist.textContent = message.artist;
            card.appendChild(artist);
        }

        cornerItems.breakMusic = {
            corner: QR_CORNERS.includes(message.corner) ? message.corner : 'bottomleft',
            node: card,
        };

        drawCorners();
    }

    function setQrCodes(message) {
        const codes = Array.isArray(message.codes) ? message.codes : [];

        // At most one is ever drawn, since the venue names the source, so the first is the whole of it.
        const code = codes.find((entry) => entry && entry.imageUrl);

        if (!code) {
            cornerItems.qr = null;
            drawCorners();
            return;
        }

        setCornerOffset(code.offset);

        const figure = doc.createElement('figure');
        figure.className = 'kh-qr';
        figure.dataset.size = QR_SIZES.includes(code.size) ? code.size : 'medium';

        // A denser code drawn in the same corner has smaller modules; below about three pixels each
        // no phone reads it, so the module count sets a floor the venue's size cannot go under.
        if (Number.isFinite(code.modules) && code.modules > 0)
            figure.style.setProperty('--kh-qr-modules', String(code.modules));

        // Already resolved host-side from the venue, so nothing here decides a default: a screen
        // guessing one is how two screens in a room end up disagreeing.
        if (Number.isFinite(code.safeZone) && code.safeZone > 0)
            figure.style.setProperty('--kh-qr-safezone', String(code.safeZone));

        const image = doc.createElement('img');
        // Decorative in the accessibility sense: nobody is reading a karaoke screen with a
        // reader, and a code carries its meaning by being one.
        image.alt = '';
        image.src = code.imageUrl;
        figure.appendChild(image);

        if (code.caption) {
            const caption = doc.createElement('figcaption');
            caption.textContent = code.caption;
            figure.appendChild(caption);
        }

        cornerItems.qr = {
            corner: QR_CORNERS.includes(code.corner) ? code.corner : 'bottomright',
            node: figure,
        };

        drawCorners();
    }

    /// The venue's colours. Each is a CSS custom property the page's rules read with their own colour as
    /// the fallback, so a null removes it and puts that part back in the page's own colour.
    function setScreenTheme(message) {
        const root = doc.documentElement.style;
        const properties = {
            '--screen-bg': message.background,
            '--intro-fg': message.introText,
            '--intro-outline': message.introOutline,
            '--next-fg': message.nextSingerText,
            '--next-name-fg': message.nextSingerName,
            '--next-panel': message.nextSingerPanel,
            '--break-fg': message.breakMusicCardText,
            '--break-bg': message.breakMusicCardBackground,
            '--qr-frame': message.qrCodeFrame,
            '--qr-caption': message.qrCodeCaption,
        };
        for (const [name, value] of Object.entries(properties)) {
            if (value) root.setProperty(name, value);
            else root.removeProperty(name);
        }

        overlay.setColours({ sung: message.lyricsSung, unsung: message.lyricsUnsung, outline: message.lyricsOutline });
    }

    function setMarquee(message) {
        if (message.enabled !== true) {
            marquee.hidden = true;
            marqueeSignature = null;
            return;
        }

        const entries = Array.isArray(message.entries) ? message.entries : [];
        const hasEntries = entries.length > 0;
        const glyph = message.dividerGlyph || '';

        // Pinned only means anything while there are names to label; a message-only band pins nothing.
        const pinned = message.pinLabel === true && hasEntries;

        // Nothing to say is not a band across the screen. A venue can leave the message empty and
        // run zero singers, and the room should just see the video.
        if (!hasEntries && !message.message) {
            marquee.hidden = true;
            marqueeSignature = null;
            return;
        }

        // A divider's own colour and glyph, so the pre-message one matches the ones between entries.
        const dividerChip = () => chip(glyph, 'marquee-sep');

        // Built as nodes, not markup: a venue types the message and a singer types their own name,
        // and neither may reach innerHTML.
        const build = () => {
            const span = doc.createElement('span');

            if (hasEntries) {
                // Held at the edge instead when pinned, so it must not also scroll past.
                if (!pinned) span.appendChild(chip('Up next', 'marquee-label'));

                entries.forEach((segment) => {
                    if (segment.kind === 'separator') {
                        if (glyph) span.appendChild(dividerChip());
                        return;
                    }

                    const className = MARQUEE_SEGMENT_CLASSES[segment.kind];
                    span.appendChild(className ? chip(segment.text, className) : doc.createTextNode(segment.text));
                });
            }

            if (message.message) {
                if (hasEntries && glyph) span.appendChild(dividerChip());
                span.appendChild(chip(message.message, 'marquee-message'));
            }

            return span;
        };

        // Rebuilt only when what it reads actually changed: swapping in identical nodes still restarts
        // the scroll. That showed as the band restarting every few seconds instead of scrolling. The
        // glyph is in here too: a shape-only edit leaves entries untouched but redraws a different
        // character into the same separator nodes.
        const signature = JSON.stringify([entries, pinned, message.message || '', glyph]);
        const contentChanged = signature !== marqueeSignature;
        marqueeSignature = signature;

        // Every copy carries the same content: the keyframes translate the track by exactly one of
        // them, so the next sits where the last began and the join never shows.
        if (contentChanged) marqueeTrack.replaceChildren(build(), build());

        marquee.dataset.position = message.position === 'top' ? 'top' : 'bottom';
        marquee.dataset.pinned = pinned ? 'true' : 'false';
        marquee.style.setProperty('--marquee-bg', message.backgroundColor || '#000000');
        marquee.style.setProperty('--marquee-fg', message.textColor || '#f2f2f5');

        // Unset takes the CSS default (today's look); each is independent of the others.
        if (message.singerColor) marquee.style.setProperty('--marquee-singer-fg', message.singerColor);
        else marquee.style.removeProperty('--marquee-singer-fg');

        if (message.songColor) marquee.style.setProperty('--marquee-song-fg', message.songColor);
        else marquee.style.removeProperty('--marquee-song-fg');

        // A chosen divider colour draws at full strength; the default currentColor is what carries the
        // dimming instead (see the stylesheet), so an explicit colour is not washed out by it too.
        if (message.dividerColor) {
            marquee.style.setProperty('--marquee-sep-fg', message.dividerColor);
            marquee.style.setProperty('--marquee-sep-opacity', '1');
        } else {
            marquee.style.removeProperty('--marquee-sep-fg');
            marquee.style.removeProperty('--marquee-sep-opacity');
        }

        // Null/undefined means never chosen, not zero: zero is a real, fully-transparent choice.
        const opacity = message.backgroundOpacityPercent;
        if (typeof opacity === 'number' && Number.isFinite(opacity))
            marquee.style.setProperty('--marquee-bg-opacity', String(Math.min(100, Math.max(0, opacity)) / 100));
        else
            marquee.style.removeProperty('--marquee-bg-opacity');

        // Zero means the venue never chose one, so the stylesheet's own size stands.
        const size = Number(message.fontSizePixels) || 0;
        if (size > 0) {
            const clamped = Math.min(MARQUEE_FONT_SIZE_MAX_PX, Math.max(MARQUEE_FONT_SIZE_MIN_PX, size));
            marquee.style.setProperty('--marquee-font-size', `${clamped}px`);
        } else {
            marquee.style.removeProperty('--marquee-font-size');
        }

        marquee.hidden = false;

        // Clamped: a speed of zero never finishes a lap, and one past the cap is unreadable.
        const chosen = Number(message.scrollSpeed) || 0;
        const speed = chosen > 0
            ? Math.min(MARQUEE_SPEED_MAX, Math.max(MARQUEE_SPEED_MIN, chosen))
            : MARQUEE_SPEED;

        // Measured after unhiding, or the track has no width to measure. Enough copies to cover the
        // screen plus one spare; a fixed two left a gap on a full-screen band with a short line.
        const copies = copiesToCoverTheBand();
        marquee.style.setProperty('--marquee-copies', String(copies));

        // One copy's width: the distance a lap actually travels, which is what the venue's speed is
        // in pixels a second of. Speed, not a lap time: a fixed duration would make a long line race
        // to keep a short one's pace.
        const distance = marqueeTrack.scrollWidth / copies;
        const duration = `${Math.max(MARQUEE_LAP_MIN_SECONDS, distance / speed)}s`;
        const durationChanged = duration !== marquee.style.getPropertyValue('--marquee-duration');
        marquee.style.setProperty('--marquee-duration', duration);

        // Otherwise new or differently-timed text inherits however far the old lap had already run.
        // The animation lives in the stylesheet; clearing it and forcing a reflow restarts its clock.
        if (contentChanged || durationChanged) {
            marqueeTrack.style.animation = 'none';
            void marqueeTrack.offsetWidth;
            marqueeTrack.style.animation = '';
        }
    }

    /// Re-tiles the track so one copy's width is never less than the band it has to cross, returns
    /// how many there are. One copy is window-wide, a fraction of one on a television.
    function copiesToCoverTheBand() {
        const first = marqueeTrack.firstElementChild;

        if (!first) return 2;

        const copyWidth = first.getBoundingClientRect().width;
        const band = marqueeViewport?.clientWidth || marquee.clientWidth;

        // A copy with no width yet, or a band with none, is nothing to divide by. Two is the old
        // behaviour and is right as soon as one copy is wider than the band anyway.
        if (!(copyWidth > 0) || !(band > 0)) return 2;

        // One to cover the band, one more to be arriving while it does.
        const wanted = Math.min(MARQUEE_COPIES_MAX, Math.ceil(band / copyWidth) + 1);

        if (wanted === marqueeTrack.childElementCount) return wanted;

        const template = first.cloneNode(true);
        const copies = [];

        for (let i = 0; i < wanted; i++) copies.push(template.cloneNode(true));

        marqueeTrack.replaceChildren(...copies);

        return wanted;
    }

    // The band's width is not fixed: a screen going full screen or a resized window leaves the
    // copies that covered it a moment ago covering only a fraction. This is the only re-measure.
    function retileMarquee() {
        if (marquee.hidden || !marqueeTrack.firstElementChild) return;

        marquee.style.setProperty('--marquee-copies', String(copiesToCoverTheBand()));
    }

    if (marqueeViewport && ResizeObserverType) {
        let pending = 0;

        // Debounced: once the drag settles is the only time worth restarting the scroll.
        const observer = new ResizeObserverType(() => {
            clearTimer(pending);
            pending = setTimer(retileMarquee, MARQUEE_RETILE_SETTLE_MS);
        });

        observer.observe(marqueeViewport);
    }

    function chip(text, className) {
        const el = doc.createElement('span');
        el.className = className;
        el.textContent = text;
        return el;
    }

    return {
        setScreenTheme,
        setMarquee,
        setQrCodes,
        setBreakMusicCard,
        showNextSinger,
        clearNextSinger,
        showIdlePicture,
    };
}
