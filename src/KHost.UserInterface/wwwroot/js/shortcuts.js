// Panel focus shortcuts. The chord is matched here rather than in a Blazor handler so that the
// keystrokes that are not shortcuts — every letter of a singer's name — never cross the circuit.
//
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

        // Typing replaces what is there — the shortcut is for starting a new search, and a host
        // who wanted to append can still click.
        if (typeof target.select === 'function') target.select();
    });
})();
