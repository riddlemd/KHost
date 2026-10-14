'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadScreen, plain } = require('./load-screen.js');

/// Enough of an element for the chrome: children, a style that keeps custom properties, classes
/// found by querySelector, and a deep clone.
function fakeElement(tagName = 'div') {
    const properties = new Map();
    return {
        tagName,
        children: [],
        dataset: {},
        hidden: false,
        textContent: '',
        className: '',
        attributes: {},
        listeners: {},
        scrollWidth: 0,
        clientWidth: 0,
        offsetWidth: 0,
        style: {
            properties,
            animation: '',
            setProperty(name, value) { properties.set(name, String(value)); },
            removeProperty(name) { properties.delete(name); },
            getPropertyValue(name) { return properties.get(name) ?? ''; },
        },
        addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); },
        appendChild(child) { this.children.push(child); return child; },
        replaceChildren(...children) { this.children = children; },
        get firstElementChild() { return this.children[0] ?? null; },
        get childElementCount() { return this.children.length; },
        getBoundingClientRect() { return { width: 0 }; },
        getAttribute(name) { return this.attributes[name] ?? null; },
        setAttribute(name, value) { this.attributes[name] = String(value); },
        removeAttribute(name) { delete this.attributes[name]; },
        querySelector(selector) { return find(this, (el) => `.${el.className}` === selector); },
        cloneNode() { return structuredClone({ ...this, style: null }); },
    };
}

function find(root, match) {
    for (const child of root.children || []) {
        if (match(child)) return child;
        const deeper = find(child, match);
        if (deeper) return deeper;
    }
    return null;
}

function text(el) {
    return el.children && el.children.length ? el.children.map(text).join('|') : el.textContent;
}

function chromePage({ stillSrc = null, visualiser = {} } = {}) {
    const screen = loadScreen();
    const corners = ['bottomright', 'bottomleft', 'topright', 'topleft'].map((corner) => {
        const el = fakeElement();
        el.dataset.corner = corner;
        return el;
    });
    const nextSinger = fakeElement();
    nextSinger.hidden = true;
    nextSinger.appendChild(Object.assign(fakeElement('span'), { className: 'kh-next__singer' }));
    nextSinger.appendChild(Object.assign(fakeElement('span'), { className: 'kh-next__song' }));

    const elements = {
        still: fakeElement('img'),
        placeholder: fakeElement(),
        nextSinger,
        marquee: fakeElement(),
        'marquee-track': fakeElement(),
        'marquee-viewport': fakeElement(),
    };
    if (stillSrc) elements.still.setAttribute('src', stillSrc);

    const root = fakeElement('html');
    const doc = {
        documentElement: root,
        getElementById: (id) => elements[id] ?? null,
        querySelectorAll: (selector) => (selector === '.kh-corner' ? corners : []),
        createElement: (tag) => fakeElement(tag),
        createTextNode: (value) => Object.assign(fakeElement('#text'), { textContent: value }),
    };

    const colours = [];
    const shown = [];
    const calls = [];
    const fakeVisualiser = {
        active: false,
        setLook() {},
        freeze() {},
        setAudio() {},
        show(look) { shown.push(plain(look)); return Promise.resolve(visualiser.showResult ?? true); },
        hide() { calls.push('hide'); },
        ...visualiser,
    };

    const chrome = screen.createScreenChrome({
        doc,
        overlay: { setColours: (c) => colours.push(plain(c)) },
        visualiser: fakeVisualiser,
        background: fakeElement('audio'),
        loadVisualiserLevels: () => {},
        elementAudioSource: () => null,
        releaseElementTap: () => {},
        ResizeObserverType: null,
    });

    const corner = (name) => corners.find((el) => el.dataset.corner === name);
    return { chrome, elements, root, corner, colours, shown, calls };
}

const marqueeVar = (elements, name) => elements.marquee.style.getPropertyValue(name);

test('setMarquee_DisabledOrWithNothingToSay_HidesTheBand', () => {
    const { chrome, elements } = chromePage();

    chrome.setMarquee({ enabled: true, message: 'Welcome' });
    assert.equal(elements.marquee.hidden, false);

    chrome.setMarquee({ enabled: false, message: 'Welcome' });
    assert.equal(elements.marquee.hidden, true, 'disabled');

    chrome.setMarquee({ enabled: true, message: 'Welcome' });
    chrome.setMarquee({ enabled: true, entries: [], message: '' });
    assert.equal(elements.marquee.hidden, true, 'no singers and no message');
});

test('setMarquee_Entries_DrawTwoLabelledCopiesWithColouredChipsAndDividers', () => {
    const { chrome, elements } = chromePage();

    chrome.setMarquee({
        enabled: true,
        dividerGlyph: '*',
        message: 'Tip your host',
        entries: [
            { kind: 'singer', text: 'Ana' },
            { kind: 'other', text: ' - ' },
            { kind: 'song', text: 'Hello' },
            { kind: 'separator' },
            { kind: 'singer', text: 'Bo' },
        ],
    });

    const track = elements['marquee-track'];
    assert.equal(track.children.length, 2);
    assert.equal(text(track.children[0]), text(track.children[1]), 'every copy carries the same content');
    assert.equal(text(track.children[0]), 'Up next|Ana| - |Hello|*|Bo|*|Tip your host');
    assert.deepEqual(track.children[0].children.map((c) => c.className),
        ['marquee-label', 'marquee-singer', '', 'marquee-song', 'marquee-sep', 'marquee-singer', 'marquee-sep', 'marquee-message']);
    assert.equal(elements.marquee.dataset.position, 'bottom');
    assert.equal(elements.marquee.dataset.pinned, 'false');
});

test('setMarquee_PinnedLabel_IsLeftOutOfTheScrollingCopies', () => {
    const { chrome, elements } = chromePage();

    chrome.setMarquee({ enabled: true, pinLabel: true, position: 'top', entries: [{ kind: 'singer', text: 'Ana' }] });

    assert.equal(text(elements['marquee-track'].children[0]), 'Ana');
    assert.equal(elements.marquee.dataset.pinned, 'true');
    assert.equal(elements.marquee.dataset.position, 'top');
});

test('setMarquee_Colours_AreSetAsCssVariablesAndRemovedWhenUnset', () => {
    const { chrome, elements } = chromePage();

    chrome.setMarquee({
        enabled: true, message: 'Hi',
        backgroundColor: '#112233', textColor: '#445566', singerColor: '#aa0000', songColor: '#00aa00',
        dividerColor: '#0000aa', backgroundOpacityPercent: 150,
    });
    assert.deepEqual(
        ['--marquee-bg', '--marquee-fg', '--marquee-singer-fg', '--marquee-song-fg', '--marquee-sep-fg', '--marquee-sep-opacity', '--marquee-bg-opacity']
            .map((name) => marqueeVar(elements, name)),
        ['#112233', '#445566', '#aa0000', '#00aa00', '#0000aa', '1', '1']);

    chrome.setMarquee({ enabled: true, message: 'Hi', backgroundOpacityPercent: 0 });
    assert.equal(marqueeVar(elements, '--marquee-bg-opacity'), '0', 'zero is a real choice');
    assert.equal(marqueeVar(elements, '--marquee-bg'), '#000000');
    assert.equal(marqueeVar(elements, '--marquee-fg'), '#f2f2f5');
    for (const name of ['--marquee-singer-fg', '--marquee-song-fg', '--marquee-sep-fg', '--marquee-sep-opacity'])
        assert.equal(marqueeVar(elements, name), '', `${name} is removed`);

    chrome.setMarquee({ enabled: true, message: 'Hi', backgroundOpacityPercent: -20 });
    assert.equal(marqueeVar(elements, '--marquee-bg-opacity'), '0');

    chrome.setMarquee({ enabled: true, message: 'Hi' });
    assert.equal(marqueeVar(elements, '--marquee-bg-opacity'), '', 'never chosen');
});

test('setMarquee_FontSize_IsClampedToTheBandAndUnsetAtZero', () => {
    const { chrome, elements } = chromePage();
    const sizeFor = (fontSizePixels) => {
        chrome.setMarquee({ enabled: true, message: 'Hi', fontSizePixels });
        return marqueeVar(elements, '--marquee-font-size');
    };

    assert.deepEqual([sizeFor(200), sizeFor(5), sizeFor(40), sizeFor(96), sizeFor(12), sizeFor(0)],
        ['96px', '12px', '40px', '96px', '12px', '']);
});

test('setMarquee_LapDuration_FollowsTheSpeedWithAFloor', () => {
    const { chrome, elements } = chromePage();
    const durationFor = (scrollWidth, scrollSpeed) => {
        elements['marquee-track'].scrollWidth = scrollWidth;
        chrome.setMarquee({ enabled: true, message: `Hi ${scrollWidth} ${scrollSpeed}`, scrollSpeed });
        return marqueeVar(elements, '--marquee-duration');
    };

    assert.equal(durationFor(1800, 90), '10s', 'one copy of two is 900px at 90px a second');
    assert.equal(durationFor(1800, 0), '10s', 'unset is the default speed');
    assert.equal(durationFor(100, 90), '4s', 'a short lap is held to the floor');
    assert.equal(durationFor(8000, 1000), '10s', 'a speed past the cap is held to it');
});

test('setQrCodes_Code_IsPlacedInItsCornerAndHiddenWhenGone', () => {
    const { chrome, corner, root } = chromePage();

    chrome.setQrCodes({ codes: [{ imageUrl: '' }, { imageUrl: 'qr.svg', corner: 'topleft', size: 'large', modules: 29, safeZone: 4, caption: 'Scan me', offset: 3 }] });

    const figure = corner('topleft').children[0];
    assert.equal(figure.className, 'kh-qr');
    assert.equal(figure.dataset.size, 'large');
    assert.equal(figure.style.getPropertyValue('--kh-qr-modules'), '29');
    assert.equal(figure.style.getPropertyValue('--kh-qr-safezone'), '4');
    assert.deepEqual(figure.children.map((c) => c.tagName), ['img', 'figcaption']);
    assert.equal(figure.children[0].src, 'qr.svg');
    assert.equal(figure.children[1].textContent, 'Scan me');
    assert.equal(root.style.getPropertyValue('--kh-corner-offset'), '3vmin');

    chrome.setQrCodes({ codes: [{ imageUrl: 'qr.svg', corner: 'middle', size: 'huge' }] });
    assert.equal(corner('topleft').children.length, 0, 'moved out of the old corner');
    const placed = corner('bottomright').children[0];
    assert.equal(placed.dataset.size, 'medium');
    assert.equal(placed.children.length, 1, 'no caption, no figcaption');
    assert.equal(placed.style.getPropertyValue('--kh-qr-modules'), '');

    chrome.setQrCodes({ codes: [] });
    assert.equal(corner('bottomright').children.length, 0);
});

test('setBreakMusicCard_Track_ShowsTitleAndArtistAboveACodeInTheSameCorner', () => {
    const { chrome, corner } = chromePage();

    chrome.setQrCodes({ codes: [{ imageUrl: 'qr.svg', corner: 'bottomleft' }] });
    chrome.setBreakMusicCard({ enabled: true, title: 'Song', artist: 'Band' });

    const [card, code] = corner('bottomleft').children;
    assert.equal(card.className, 'kh-break-music');
    assert.equal(text(card), 'Song|Band');
    assert.equal(code.className, 'kh-qr', 'the card sits above the code');

    chrome.setBreakMusicCard({ enabled: true, title: 'Advert', corner: 'topright' });
    assert.equal(text(corner('topright').children[0]), 'Advert', 'no artist, no second line');
    assert.equal(corner('topright').children[0].children.length, 1);

    chrome.setBreakMusicCard({ enabled: true, title: '' });
    assert.equal(corner('topright').children.length, 0, 'no title, no card');
    assert.deepEqual(corner('bottomleft').children.map((c) => c.className), ['kh-qr'], 'not even in the default corner');
    chrome.setBreakMusicCard({ enabled: true, title: 'Song' });
    chrome.setBreakMusicCard({ enabled: false, title: 'Song' });
    assert.deepEqual(corner('bottomleft').children.map((c) => c.className), ['kh-qr']);
});

test('setScreenTheme_Colours_AreSetOnTheRootAndRemovedWhenNull', () => {
    const { chrome, root, colours } = chromePage();

    chrome.setScreenTheme({ background: '#101010', qrCodeCaption: '#202020', lyricsSung: '#ff0000', lyricsOutline: '#000000' });
    assert.equal(root.style.getPropertyValue('--screen-bg'), '#101010');
    assert.equal(root.style.getPropertyValue('--qr-caption'), '#202020');
    assert.equal(root.style.getPropertyValue('--next-panel'), '');

    chrome.setScreenTheme({ background: null, nextSingerPanel: '#303030' });
    assert.equal(root.style.getPropertyValue('--screen-bg'), '', 'null puts the page colour back');
    assert.equal(root.style.getPropertyValue('--qr-caption'), '');
    assert.equal(root.style.getPropertyValue('--next-panel'), '#303030');
    assert.deepEqual(colours[0], { sung: '#ff0000', outline: '#000000' });
});

test('showNextSinger_OverThePicture_ShowsTheCardAndTheVenuesStillThenClearGoesBack', () => {
    const { chrome, elements } = chromePage({ stillSrc: 'venue.png' });
    elements.still.hidden = true;

    chrome.showNextSinger({ singer: 'Ana', song: 'Hello', artist: 'Adele', background: 'sideways' });

    const card = elements.nextSinger;
    assert.equal(card.hidden, false);
    assert.equal(card.dataset.background, 'over');
    assert.equal(card.querySelector('.kh-next__singer').textContent, 'Ana');
    assert.equal(card.querySelector('.kh-next__song').textContent, 'Hello - Adele');
    assert.deepEqual([elements.still.hidden, elements.placeholder.hidden], [false, true]);

    chrome.showNextSinger({ singer: 'Bo', background: 'blackout' });
    assert.equal(card.querySelector('.kh-next__song').hidden, true, 'nothing queued, no song line');
    assert.equal(card.dataset.background, 'blackout');
    assert.deepEqual([elements.still.hidden, elements.placeholder.hidden], [true, true]);

    chrome.clearNextSinger();
    assert.equal(card.hidden, true);
    assert.deepEqual([elements.still.hidden, elements.placeholder.hidden], [false, true]);
});

test('clearNextSinger_NoStill_PutsThePlaceholderBack', () => {
    const { chrome, elements } = chromePage();

    chrome.showNextSinger({ singer: 'Ana', background: 'blackout' });
    chrome.clearNextSinger();

    assert.deepEqual([elements.still.hidden, elements.placeholder.hidden], [true, false]);
});

test('showNextSinger_Visualisation_DrawsTheLookWithoutAVideoAndFallsBackWhenItCannot', async () => {
    const { chrome, elements, shown, calls } = chromePage({ visualiser: { showResult: false } });

    chrome.showNextSinger({ singer: 'Ana', background: 'visualisation', visualiser: { preset: 'x', videoUrl: 'v.mp4' } });
    assert.deepEqual(shown, [{ preset: 'x' }], 'the card never plays a video');
    assert.equal(elements.nextSinger.dataset.background, 'visualisation');

    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(elements.nextSinger.dataset.background, 'over', 'one that cannot be drawn leaves the card over the picture');
    assert.equal(elements.placeholder.hidden, false);

    chrome.clearNextSinger();
    assert.deepEqual(calls, [], 'the card no longer owns a visualiser to take down');
});

test('clearNextSinger_CardVisualisationUp_TakesItDownButLeavesASongsAlone', async () => {
    const page = chromePage();
    page.chrome.showNextSinger({ singer: 'Ana', background: 'visualisation', visualiser: { preset: 'x' } });
    page.chrome.clearNextSinger();
    assert.deepEqual(page.calls, ['hide']);

    const song = chromePage({ visualiser: { active: true } });
    song.chrome.showNextSinger({ singer: 'Ana', background: 'visualisation', visualiser: { preset: 'x' } });
    song.chrome.clearNextSinger();
    assert.deepEqual([song.shown, song.calls], [[], []], 'a song\'s visualiser is the song\'s');
});

test('setMarquee_ColourOnlyResend_KeepsTheSameNodesSoTheScrollRunsOn', () => {
    const { chrome, elements } = chromePage();
    const entries = [{ kind: 'singer', text: 'Ana' }];

    chrome.setMarquee({ enabled: true, entries, textColor: '#111111' });
    const first = elements['marquee-track'].children[0];
    chrome.setMarquee({ enabled: true, entries, textColor: '#222222' });
    assert.equal(elements['marquee-track'].children[0], first);

    chrome.setMarquee({ enabled: true, entries: [{ kind: 'singer', text: 'Bo' }] });
    assert.notEqual(elements['marquee-track'].children[0], first);
});

test('clearNextSinger_CardAlreadyDown_LeavesThePictureAlone', () => {
    const { chrome, elements } = chromePage();
    elements.placeholder.hidden = true;

    chrome.clearNextSinger();

    assert.deepEqual([elements.still.hidden, elements.placeholder.hidden], [false, true], 'a song playing keeps its screen');
});

test('showNextSinger_VisualisationThatDraws_StaysUntilAnotherAnnouncementTakesItDown', async () => {
    const { chrome, elements, calls } = chromePage();

    chrome.showNextSinger({ singer: 'Ana', background: 'visualisation', visualiser: { preset: 'x' } });
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(elements.nextSinger.dataset.background, 'visualisation');
    assert.deepEqual([elements.still.hidden, elements.placeholder.hidden], [true, true]);

    chrome.showNextSinger({ singer: 'Bo', background: 'over' });
    assert.deepEqual(calls, ['hide']);
});
