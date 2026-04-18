// Drag-to-resize for the KHost panel layout.
// Persists sizes to localStorage. Returns a { dispose } object for cleanup.

const KEYS = {
    queueWidth:   'khost-queue-width',
    searchHeight: 'khost-search-height',
};

export function init() {
    const colLeft    = document.getElementById('panel-queue');
    const nowPlaying = document.getElementById('panel-nowplaying');
    const songSearch = document.getElementById('panel-search');
    const vHandle    = document.getElementById('resize-v');
    const hHandle   = document.getElementById('resize-h');
    const body       = document.getElementById('khost-body');

    if (!colLeft || !nowPlaying || !songSearch || !vHandle || !hHandle || !body)
        return { dispose: () => {} };

    // ── Restore saved sizes ───────────────────────────────────────────────
    const savedQW = parseFloat(localStorage.getItem(KEYS.queueWidth));
    if (savedQW > 0) colLeft.style.width = savedQW + 'px';

    const savedSH = parseFloat(localStorage.getItem(KEYS.searchHeight));
    if (savedSH > 0) {
        songSearch.style.flex   = 'none';
        songSearch.style.height = savedSH + 'px';
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
            if (songSearch.style.height)
                localStorage.setItem(KEYS.searchHeight, parseFloat(songSearch.style.height));
        }, 150);
    }

    // ── Handlers ──────────────────────────────────────────────────────────
    function beginV(e) {
        mode       = 'v';
        startCoord = clientCoords(e).x;
        startSize  = colLeft.getBoundingClientRect().width;
        vHandle.classList.add('dragging');
        lock('col-resize');
        e.preventDefault();
    }

    function beginH(e) {
        mode       = 'h';
        startCoord = clientCoords(e).y;
        startSize  = songSearch.getBoundingClientRect().height;
        hHandle.classList.add('dragging');
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
            songSearch.style.flex   = 'none';
            songSearch.style.height = newH + 'px';
        }

        scheduleSave();
    }

    function end() {
        if (!mode) return;
        vHandle.classList.remove('dragging');
        hHandle.classList.remove('dragging');
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
