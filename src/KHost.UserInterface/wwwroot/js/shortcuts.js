// Panel focus shortcuts, matched here so ordinary typing never crosses the Blazor circuit.
// Accel is Ctrl or Cmd: the hints read "ctrl+1", but a host on a Mac reaches for Cmd.
(function () {
    const targets = {
        '1': 'new-singer',
        '2': 'media-search'
    };

    document.addEventListener('keydown', function (event) {
        if (!(event.ctrlKey || event.metaKey) || event.altKey || event.shiftKey) return;

        const name = targets[event.key];
        if (!name) return;

        const target = document.querySelector('[data-kh-shortcut="' + name + '"]');

        // Off the console the panel is not rendered; leave the key to whatever else wants it.
        if (!target) return;

        event.preventDefault();
        target.focus();

        // Typing replaces what is there. The shortcut is for starting a new search, and a host
        // who wanted to append can still click.
        if (typeof target.select === 'function') target.select();
    });
})();

// Arrow keys inside a keyboard-navigable list. Blazor's own handler still runs; preventDefault only
// cancels the browser's default, but without it macOS beeps and the list scrolls under the selection.
(function () {
    const ARROWS = new Set(['ArrowUp', 'ArrowDown']);

    document.addEventListener('keydown', function (event) {
        if (!ARROWS.has(event.key) || event.ctrlKey || event.metaKey || event.altKey) return;

        if (event.target instanceof Element && event.target.closest('[data-kh-keylist]'))
            event.preventDefault();
    });
})();

// Ctrl/Cmd+Enter plays the next queued song; add Shift and it announces the next singer instead.
// Enter carries no OS/browser default the way a digit or a letter does, which is why the two "go"
// actions get it rather than a mnemonic letter that Chrome or the OS already owns.
(function () {
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey) || event.altKey) return;

        const target = event.target;
        if (target instanceof HTMLElement) {
            const tag = target.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable)
                return;
        }

        const name = event.shiftKey ? 'announce-next-singer' : 'play-next';
        const button = document.querySelector('[data-kh-shortcut="' + name + '"]');

        // Not rendered (nothing queued, nobody to announce, or a song is already loaded): leave the
        // key alone, the same fallback every other shortcut here uses for a target that may be absent.
        if (!button) return;

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

        // Already open: closing it is the toggle, not a second copy stacked on top.
        const closeBtn = document.querySelector('.kh-help-dialog .kh-dialog__close-btn');
        const button = closeBtn ?? document.querySelector('[data-kh-shortcut="help"]');
        if (!button) return;

        event.preventDefault();
        button.click();
    });
})();
