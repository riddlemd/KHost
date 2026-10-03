// Global console chords, matched here so ordinary typing never crosses the Blazor circuit. Each one
// focuses or clicks a [data-kh-shortcut] element, so the element's own guards still decide.
// Accel is Ctrl or Cmd: the hints read "ctrl", but a host on a Mac reaches for Cmd.
// Which browser shortcuts are cancelled is browser-keys.js's business, not this file's.
// None of them reach the page behind an open dialog: a chord there moves focus, or the queue, out
// from under the question the dialog is asking.
(function () {
    // Matched on the key's position, not its character: AZERTY's Ctrl+1 reports key "&".
    const focusTargets = {
        Digit1: 'new-singer', Numpad1: 'new-singer',
        Digit2: 'media-search', Numpad2: 'media-search',
        Digit3: 'singer-queue', Numpad3: 'singer-queue',
        Digit4: 'singer-songs', Numpad4: 'singer-songs'
    };

    document.addEventListener('keydown', function (event) {
        if (!(event.ctrlKey || event.metaKey) || event.altKey || event.shiftKey) return;

        const name = focusTargets[event.code];
        if (!name || document.querySelector('.kh-scrim')) return;

        const target = document.querySelector('[data-kh-shortcut="' + name + '"]');

        // Off the console, or nothing to focus (no singers, nobody selected): leave the key alone.
        if (!target) return;

        event.preventDefault();
        target.focus();

        // Typing replaces what is there. The shortcut is for starting a new search, and a host
        // who wanted to append can still click.
        if (typeof target.select === 'function') target.select();
    });
})();

// Keys inside a keyboard-navigable list. Blazor's own handler still runs; preventDefault only cancels
// the browser's default — without it macOS beeps and the list scrolls under the selection.
(function () {
    const LIST_KEYS = new Set(['ArrowUp', 'ArrowDown', 'Delete', 'Backspace']);

    document.addEventListener('keydown', function (event) {
        if (!LIST_KEYS.has(event.key) || event.ctrlKey || event.metaKey || event.altKey) return;

        if (event.target instanceof Element && event.target.closest('[data-kh-keylist]'))
            event.preventDefault();
    });

    // A focused button inside a list activates on Enter by itself; the list's own Enter (queue the
    // selected search result) must not run on top of it, or one press does two things.
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Enter' || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
        if (!(event.target instanceof Element)) return;

        const list = event.target.closest('[data-kh-keylist]');
        if (list && list !== event.target) event.stopPropagation();
    }, { capture: true });
})();

// Transport: Ctrl/Cmd+Enter plays the next queued song, add Shift to announce the next singer, add
// Alt to pause or resume; Ctrl/Cmd+Backspace stops. Outside text fields only, where Enter submits and
// Backspace deletes a word. Enter carries no OS/browser default the way a digit or a letter does,
// which is why the "go" actions get it rather than a letter Chrome or the OS already owns.
(function () {
    function chord(event) {
        if (!(event.ctrlKey || event.metaKey)) return null;

        if (event.key === 'Enter') {
            if (event.altKey) return event.shiftKey ? null : 'pause-resume';
            return event.shiftKey ? 'announce-next-singer' : 'play-next';
        }

        // Not with Shift: Cmd+Shift+Delete clears browsing data in Chrome and empties the Trash.
        if (event.key === 'Backspace' && !event.altKey && !event.shiftKey) return 'stop';

        return null;
    }

    function isTextEntry(element) {
        return window.KHostBrowserKeys
            ? window.KHostBrowserKeys.isTextEntry(element)
            : element.tagName === 'INPUT' || element.tagName === 'TEXTAREA' || element.isContentEditable;
    }

    document.addEventListener('keydown', function (event) {
        const name = chord(event);
        if (!name || document.querySelector('.kh-scrim')) return;

        const target = event.target;
        if (target instanceof HTMLElement) {
            const tag = target.tagName;
            const skip = name === 'stop'
                ? isTextEntry(target)
                : tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
            if (skip) return;
        }

        const button = document.querySelector('[data-kh-shortcut="' + name + '"]');

        // Not rendered or disabled (nothing queued, nobody to announce, nothing loaded, already
        // stopping): leave the key alone, as for every other target that may be absent.
        if (!button || button.disabled) return;

        event.preventDefault();
        button.click();
    });
})();

// "?" opens help. Matched on event.key rather than the physical key code, since "?" is Shift+/ on a
// US layout and the code alone can't tell that apart from a bare "/". Clicking the header button
// that is already wired up avoids a second JSInterop path just for this one chord.
(function () {
    document.addEventListener('keydown', function (event) {
        if (event.key !== '?' || event.ctrlKey || event.metaKey || event.altKey) return;

        const target = event.target;
        if (target instanceof HTMLElement) {
            const tag = target.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable)
                return;
        }

        // Already open: closing it is the toggle, not a second copy stacked on top. Over any other
        // dialog it does nothing.
        const closeBtn = document.querySelector('.kh-help-dialog .kh-dialog__close-btn');
        if (!closeBtn && document.querySelector('.kh-scrim')) return;

        const button = closeBtn ?? document.querySelector('[data-kh-shortcut="help"]');
        if (!button) return;

        event.preventDefault();
        button.click();
    });
})();
