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
