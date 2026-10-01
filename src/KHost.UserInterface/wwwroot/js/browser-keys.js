// The one place that decides which browser and webview shortcuts a KHost page cancels, for the
// console (native window or a browser tab) and the LocalScreen window alike. docs/browser-shortcuts.md
// is the table of what each one would do to a running show and why it is or is not blocked here.
//
// <html data-kh-surface="native"> is the Photino window; anything else is a browser tab, where the
// browser keeps what is its own (new/close tab and window are never touched). data-kh-devtools="block"
// also cancels the devtools chords; without it they are left to the webview's own switch.
//
// preventDefault only, never stopPropagation: the page's own handlers still see the key, which is
// how Alt+Left/Right in the song search and Backspace in a keyboard list reach Blazor.
(function (root) {
    const editableTypes = new Set([
        'text', 'search', 'email', 'password', 'number', 'tel', 'url', ''
    ]);

    // Where a key is text editing: a field being typed into, not a checkbox or a button.
    function isTextEntry(element) {
        if (!element) return false;
        if (element.isContentEditable) return true;

        const tag = element.tagName;
        if (tag === 'TEXTAREA') return !element.readOnly && !element.disabled;
        if (tag !== 'INPUT') return false;

        return !element.readOnly && !element.disabled
            && editableTypes.has((element.getAttribute('type') || 'text').toLowerCase());
    }

    function closest(element, selector) {
        return element && typeof element.closest === 'function' ? element.closest(selector) : null;
    }

    // Returns the reason a key is cancelled, or null to leave it alone. Pure, so the harness can
    // drive it with plain objects.
    function decide(event, options) {
        const native = options.surface === 'native';
        const key = event.key;
        const code = event.code || '';
        const lower = typeof key === 'string' ? key.toLowerCase() : '';
        const accel = event.ctrlKey || event.metaKey;
        const target = event.target;
        const inText = isTextEntry(target);

        // Windows reports AltGr as Ctrl+Alt, and AltGr+letter types a character on many layouts.
        const altGr = event.ctrlKey && event.altKey && !event.metaKey;

        if (native && options.devtools === 'block') {
            if (key === 'F12') return 'devtools';
            const devLetter = ['KeyI', 'KeyJ', 'KeyC'].includes(code) || ['i', 'j', 'c'].includes(lower);
            if (accel && (event.altKey || event.shiftKey) && devLetter && !(inText && altGr)) return 'devtools';
            if (accel && !event.altKey && !event.shiftKey && lower === 'u') return 'devtools';
        }

        // Reload throws away the Blazor circuit mid-show, and the screen's player with it.
        if (key === 'F5') return 'reload';
        if (accel && !event.altKey && lower === 'r') return 'reload';

        // Find, find next/previous: a bar over the console the host did not ask for.
        if (key === 'F3') return 'find';
        if (accel && !event.altKey && (lower === 'f' || lower === 'g')) return 'find';

        if (accel && !event.altKey && lower === 'p') return 'print';
        if (accel && !event.altKey && lower === 's') return 'save';

        // Zoom: by character and by key position, since Cmd/Ctrl with +/- moves around by layout.
        if (accel && !event.altKey) {
            if (['+', '-', '=', '_'].includes(key)) return 'zoom';
            if (['Equal', 'Minus', 'NumpadAdd', 'NumpadSubtract'].includes(code)) return 'zoom';
            if (key === '0' || code === 'Digit0' || code === 'Numpad0') return 'zoom';
        }

        // Select-all outside a field highlights the whole console; in a field it is editing.
        if (native && accel && !event.altKey && !event.shiftKey && lower === 'a' && !inText) return 'select-all';

        // Back and forward: Cmd+[ / ], Cmd+arrows outside a field (Chrome on a Mac), Alt+arrows
        // (Windows and Linux, and in a field there too).
        if (accel && (key === '[' || key === ']' || code === 'BracketLeft' || code === 'BracketRight')) return 'history';

        if (key === 'ArrowLeft' || key === 'ArrowRight') {
            if (event.altKey && !accel) {
                // The song search uses Alt+arrows itself; cancelled so nothing else does.
                if (closest(target, '[data-kh-alt-arrows]')) return 'history';
                // On a Mac Option+arrow in a field is a word jump and never navigates.
                if (inText && options.isMac) return null;
                return 'history';
            }
            if (accel && !event.altKey && !inText) return 'history';
        }

        // Backspace still navigates back in some webviews. A modified one is Mod+Backspace (stop the
        // song), and in a keyboard list a plain one removes the row; neither is navigation.
        if (key === 'Backspace' && !accel && !event.altKey && !event.shiftKey
            && !inText && !closest(target, '[data-kh-keylist]')) return 'history';

        return null;
    }

    function options() {
        const html = root.document && root.document.documentElement;
        const data = (html && html.dataset) || {};
        const platform = (root.navigator && (root.navigator.platform || root.navigator.userAgent)) || '';

        return {
            surface: data.khSurface === 'native' ? 'native' : 'browser',
            devtools: data.khDevtools === 'block' ? 'block' : 'allow',
            isMac: /Mac|iPhone|iPad/.test(platform)
        };
    }

    root.KHostBrowserKeys = { decide: decide, isTextEntry: isTextEntry };

    if (!root.document) return;

    root.document.addEventListener('keydown', function (event) {
        if (decide(event, options())) event.preventDefault();
    }, { capture: true });

    // Ctrl+wheel is pinch-zoom on a trackpad as well as Ctrl+scroll.
    root.document.addEventListener('wheel', function (event) {
        if (event.ctrlKey) event.preventDefault();
    }, { passive: false });

    // WebKit's own pinch gesture, which arrives as gesture events rather than a Ctrl+wheel.
    root.document.addEventListener('gesturestart', function (event) {
        event.preventDefault();
    });
})(typeof window !== 'undefined' ? window : globalThis);
