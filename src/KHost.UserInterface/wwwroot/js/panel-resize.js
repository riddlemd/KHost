// Nothing here may hold an element captured at startup: the now-playing, search and singer-info
// panels mount only once a singer is selected, so on a fresh page none of them exist yet. Handles
// are reached by delegation and panels are looked up per drag.

const KEYS = {
    queueWidth:   'khost-queue-width',
    searchHeight: 'khost-search-height',
};

const SELECTORS = {
    body:       '.kh-app__body',
    queue:      '.kh-app__col--queue',
    nowPlaying: '.kh-now-playing-panel',
    search:     '.kh-media-search-panel',
    vHandle:    '.kh-vertical-handle',
    hHandle:    '.kh-horizontal-handle',
};

const MinQueueWidth = 150;
const MaxQueueFraction = 0.65;
const MinPanelHeight = 80;

const find = selector => document.querySelector(selector);

export function init() {
    const body = find(SELECTORS.body);
    if (!body) return { dispose: () => {} };

    let mode = null;   // 'v' | 'h'
    let handle = null;
    let startCoord = 0;
    let startSize = 0;
    let saveTimer = null;

    // The stored width is the preference; what gets applied is that preference clamped to the
    // window as it is now. Clamping without storing means a session at 720px does not throw away
    // the width chosen at 1440.
    function applyQueueWidth() {
        const queue = find(SELECTORS.queue);
        const desired = parseFloat(localStorage.getItem(KEYS.queueWidth));
        if (!queue || !(desired > 0)) return;

        const max = body.getBoundingClientRect().width * MaxQueueFraction;
        queue.style.width = Math.max(MinQueueWidth, Math.min(desired, max)) + 'px';
    }

    function restoreSearchHeight() {
        const search = find(SELECTORS.search);
        const saved = parseFloat(localStorage.getItem(KEYS.searchHeight));

        // An inline height means this panel has already been sized — either restored or dragged —
        // and re-applying the stored value mid-session would undo the drag in progress.
        if (mode || !search || !(saved > 0) || search.style.height) return;

        search.style.flex = 'none';
        search.style.height = saved + 'px';
    }

    applyQueueWidth();
    restoreSearchHeight();

    // Selecting a singer mounts the right-hand panels long after init ran.
    const observer = new MutationObserver(restoreSearchHeight);
    observer.observe(body, { childList: true, subtree: true });

    function scheduleSave() {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(() => {
            const queue = find(SELECTORS.queue);
            const search = find(SELECTORS.search);

            if (queue) localStorage.setItem(KEYS.queueWidth, queue.getBoundingClientRect().width);
            if (search?.style.height) localStorage.setItem(KEYS.searchHeight, parseFloat(search.style.height));
        }, 150);
    }

    function clientCoords(e) {
        const src = e.touches?.[0] ?? e;
        return { x: src.clientX, y: src.clientY };
    }

    function begin(e) {
        const target = e.target instanceof Element
            ? e.target.closest(`${SELECTORS.vHandle}, ${SELECTORS.hHandle}`)
            : null;
        if (!target) return;

        const vertical = target.matches(SELECTORS.vHandle);
        const panel = find(vertical ? SELECTORS.queue : SELECTORS.search);
        if (!panel) return;

        const { x, y } = clientCoords(e);
        const rect = panel.getBoundingClientRect();

        mode = vertical ? 'v' : 'h';
        handle = target;
        startCoord = vertical ? x : y;
        startSize = vertical ? rect.width : rect.height;

        target.classList.add(vertical ? 'kh-vertical-handle--dragging' : 'kh-horizontal-handle--dragging');
        lock(vertical ? 'col-resize' : 'row-resize');
        e.preventDefault();
    }

    function move(e) {
        if (!mode) return;
        const { x, y } = clientCoords(e);

        if (mode === 'v') {
            const queue = find(SELECTORS.queue);
            if (!queue) return;

            const totalW = body.getBoundingClientRect().width;
            const newW = Math.max(MinQueueWidth, Math.min(startSize + (x - startCoord), totalW * MaxQueueFraction));
            queue.style.width = newW + 'px';

        } else {
            const search = find(SELECTORS.search);
            const nowPlaying = find(SELECTORS.nowPlaying);
            const hHandle = find(SELECTORS.hHandle);
            if (!search || !nowPlaying || !hHandle) return;

            const available = body.getBoundingClientRect().height
                - nowPlaying.getBoundingClientRect().height
                - hHandle.getBoundingClientRect().height;
            const newH = Math.max(MinPanelHeight, Math.min(startSize + (y - startCoord), available - MinPanelHeight));

            search.style.flex = 'none';
            search.style.height = newH + 'px';
        }

        scheduleSave();
    }

    function end() {
        if (!mode) return;

        handle?.classList.remove('kh-vertical-handle--dragging', 'kh-horizontal-handle--dragging');
        mode = null;
        handle = null;
        unlock();
    }

    function lock(cursor) {
        document.body.style.cursor = cursor;
        document.body.style.userSelect = 'none';
    }

    function unlock() {
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
    }

    document.addEventListener('mousedown', begin);
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', end);

    // Without this the clamp only ever runs mid-drag, so a width restored from a wider session
    // keeps the whole detail column off-screen.
    window.addEventListener('resize', applyQueueWidth);

    document.addEventListener('touchstart', begin, { passive: false });
    document.addEventListener('touchmove', move, { passive: false });
    document.addEventListener('touchend', end);

    return {
        dispose: () => {
            clearTimeout(saveTimer);
            observer.disconnect();

            document.removeEventListener('mousedown', begin);
            document.removeEventListener('mousemove', move);
            document.removeEventListener('mouseup', end);

            window.removeEventListener('resize', applyQueueWidth);

            document.removeEventListener('touchstart', begin);
            document.removeEventListener('touchmove', move);
            document.removeEventListener('touchend', end);
        }
    };
}
