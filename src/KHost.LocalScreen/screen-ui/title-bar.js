// The window is chromeless on every OS, so its title bar, its buttons and its resize edges are
// drawn here and carried out by WindowChrome in .NET. Dragging is driven from the pointer rather
// than CSS app-region: WKWebView and WebKitGTK have no such region, and WebView2 honours it only
// with a setting Photino does not expose.

/// Pointer travel before a press on the bar becomes a drag. Without it, the first press of a
/// double-click on a maximised window would restore it before the second press arrives.
const TITLE_BAR_DRAG_THRESHOLD = 3;

function createTitleBar({ doc, send, requestFrame }) {
    const root = doc.documentElement;
    const bar = doc.getElementById('titlebar');
    const title = bar.querySelector('.kh-titlebar__title');
    const maximiseButton = bar.querySelector('[data-window-action="maximise"]');

    let fullScreen = false;
    let gesture = null;
    let frameQueued = false;

    title.textContent = doc.title;

    for (const button of bar.querySelectorAll('[data-window-action]')) {
        button.addEventListener('click', () => send({ type: `window-${button.dataset.windowAction}` }));
    }

    // Stopped here so the page's own double-click, which toggles full screen, never sees it.
    bar.addEventListener('dblclick', (e) => {
        e.stopPropagation();
        if (!e.target.closest('button')) send({ type: 'window-maximise' });
    });

    bar.addEventListener('pointerdown', (e) => {
        if (e.button !== 0 || e.target.closest('button')) return;
        begin(e, bar, null);
    });

    for (const edge of doc.querySelectorAll('.kh-resize-edge')) {
        edge.addEventListener('dblclick', (e) => e.stopPropagation());
        edge.addEventListener('pointerdown', (e) => {
            if (e.button !== 0) return;
            begin(e, edge, edge.dataset.edge);
        });
    }

    function begin(e, element, edge) {
        if (fullScreen) return;

        // Captured so the moves keep coming when the pointer outruns the window it is moving.
        element.setPointerCapture?.(e.pointerId);

        gesture = {
            element,
            pointerId: e.pointerId,
            edge,
            origin: {
                screenX: e.screenX,
                screenY: e.screenY,
                clientX: e.clientX,
                innerWidth: doc.defaultView?.innerWidth ?? 0,
            },
            started: false,
            latest: null,
        };

        // An edge is a resize from the first pixel; the bar waits to tell a drag from a click.
        if (edge) start();

        element.addEventListener('pointermove', onMove);
        element.addEventListener('pointerup', onEnd);
        element.addEventListener('pointercancel', onEnd);
    }

    function start() {
        gesture.started = true;
        const message = { type: 'window-drag', phase: 'start', ...gesture.origin };
        if (gesture.edge) message.edge = gesture.edge;
        send(message);
    }

    function onMove(e) {
        if (!gesture || e.pointerId !== gesture.pointerId) return;

        if (!gesture.started) {
            const travel = Math.max(
                Math.abs(e.screenX - gesture.origin.screenX),
                Math.abs(e.screenY - gesture.origin.screenY));
            if (travel < TITLE_BAR_DRAG_THRESHOLD) return;
            start();
        }

        gesture.latest = { screenX: e.screenX, screenY: e.screenY };

        // One move a frame: a pointer reports far faster than a window can be placed.
        if (frameQueued) return;
        frameQueued = true;
        requestFrame(flushMove);
    }

    function flushMove() {
        frameQueued = false;
        if (!gesture || !gesture.latest) return;

        send({ type: 'window-drag', phase: 'move', ...gesture.latest });
        gesture.latest = null;
    }

    function onEnd(e) {
        if (!gesture || e.pointerId !== gesture.pointerId) return;
        finish();
    }

    function finish() {
        const ended = gesture;
        gesture = null;

        ended.element.removeEventListener('pointermove', onMove);
        ended.element.removeEventListener('pointerup', onEnd);
        ended.element.removeEventListener('pointercancel', onEnd);

        if (!ended.started) return;

        // The last position the pointer reached, so a quick release lands where it was let go.
        if (ended.latest) send({ type: 'window-drag', phase: 'move', ...ended.latest });
        send({ type: 'window-drag', phase: 'end' });
    }

    return {
        get isFullScreen() { return fullScreen; },

        /// From WindowChrome: no bar or edges in full screen, which is how a room runs.
        applyState(message) {
            const wasFullScreen = fullScreen;
            fullScreen = message.fullScreen === true;

            root.dataset.fullscreen = String(fullScreen);
            root.dataset.maximised = String(message.maximised === true);

            const label = message.maximised === true ? 'Restore' : 'Maximise';
            maximiseButton.title = label;
            maximiseButton.setAttribute('aria-label', label);

            if (fullScreen && gesture) finish();

            // The stage grows by the bar's height without the window necessarily changing size, and
            // the canvases only re-measure on a resize.
            if (wasFullScreen !== fullScreen) doc.defaultView?.dispatchEvent(new doc.defaultView.Event('resize'));
        },
    };
}
