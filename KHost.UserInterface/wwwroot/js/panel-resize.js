// Drag-to-resize for the KHost panel layout.
// Persists sizes to localStorage. Returns a { dispose } object for cleanup.

const KEYS = {
    queueWidth:   'khost-queue-width',
    searchHeight: 'khost-search-height',
};

export function init() {
    const colLeft    = document.querySelector('.kh-app__col--queue');
    const nowPlaying = document.querySelector('.kh-app__nowplaying');
    const MediaSearch = document.querySelector('.kh-app__search');
    const vHandle    = document.querySelector('.kh-resize-handle--v');
    const hHandle    = document.querySelector('.kh-resize-handle--h');
    const body       = document.querySelector('.kh-app__body');

    if (!colLeft || !nowPlaying || !MediaSearch || !vHandle || !hHandle || !body)
        return { dispose: () => {} };

    // ── Restore saved sizes ───────────────────────────────────────────────
    const savedQW = parseFloat(localStorage.getItem(KEYS.queueWidth));
    if (savedQW > 0) colLeft.style.width = savedQW + 'px';

    const savedSH = parseFloat(localStorage.getItem(KEYS.searchHeight));
    if (savedSH > 0) {
        MediaSearch.style.flex   = 'none';
        MediaSearch.style.height = savedSH + 'px';
    }

    // ── Drag state ────────────────────────────────────────────────────────
    let mode       = null;   // 'v' | 'h'
    let startCoord = 0;
    let startSize  = 0;
    let saveTimer  = null;

    function clientCoords(e) {
        const src = e.touches?.[0] ?? e;
        return { x: src.clientX, y: src.clientY };
    }

    // ── Persist (debounced) ───────────────────────────────────────────────
    function scheduleSave() {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(() => {
            localStorage.setItem(KEYS.queueWidth,   colLeft.getBoundingClientRect().width);
            if (MediaSearch.style.height)
                localStorage.setItem(KEYS.searchHeight, parseFloat(MediaSearch.style.height));
        }, 150);
    }

    // ── Handlers ──────────────────────────────────────────────────────────
    function beginV(e) {
        mode       = 'v';
        startCoord = clientCoords(e).x;
        startSize  = colLeft.getBoundingClientRect().width;
        vHandle.classList.add('kh-resize-handle--dragging');
        lock('col-resize');
        e.preventDefault();
    }

    function beginH(e) {
        mode       = 'h';
        startCoord = clientCoords(e).y;
        startSize  = MediaSearch.getBoundingClientRect().height;
        hHandle.classList.add('kh-resize-handle--dragging');
        lock('row-resize');
        e.preventDefault();
    }

    function move(e) {
        if (!mode) return;
        const { x, y } = clientCoords(e);

        if (mode === 'v') {
            const totalW = body.getBoundingClientRect().width;
            const newW   = Math.max(150, Math.min(startSize + (x - startCoord), totalW * 0.65));
            colLeft.style.width = newW + 'px';

        } else if (mode === 'h') {
            const bodyH     = body.getBoundingClientRect().height;
            const npH       = nowPlaying.getBoundingClientRect().height;
            const handleH   = hHandle.getBoundingClientRect().height;
            const available = bodyH - npH - handleH;
            const newH      = Math.max(80, Math.min(startSize + (y - startCoord), available - 80));
            MediaSearch.style.flex   = 'none';
            MediaSearch.style.height = newH + 'px';
        }

        scheduleSave();
    }

    function end() {
        if (!mode) return;
        vHandle.classList.remove('kh-resize-handle--dragging');
        hHandle.classList.remove('kh-resize-handle--dragging');
        mode = null;
        unlock();
    }

    function lock(cursor) {
        document.body.style.cursor     = cursor;
        document.body.style.userSelect = 'none';
    }
    function unlock() {
        document.body.style.cursor     = '';
        document.body.style.userSelect = '';
    }

    // ── Wire up events ────────────────────────────────────────────────────
    vHandle.addEventListener('mousedown',  beginV);
    hHandle.addEventListener('mousedown', beginH);
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup',   end);

    vHandle.addEventListener('touchstart',  beginV,  { passive: false });
    hHandle.addEventListener('touchstart', beginH, { passive: false });
    document.addEventListener('touchmove',  move,    { passive: false });
    document.addEventListener('touchend',   end);

    return {
        dispose: () => {
            clearTimeout(saveTimer);
            vHandle.removeEventListener('mousedown',  beginV);
            hHandle.removeEventListener('mousedown', beginH);
            document.removeEventListener('mousemove', move);
            document.removeEventListener('mouseup',   end);
            vHandle.removeEventListener('touchstart',  beginV);
            hHandle.removeEventListener('touchstart', beginH);
            document.removeEventListener('touchmove',  move);
            document.removeEventListener('touchend',   end);
        }
    };
}
