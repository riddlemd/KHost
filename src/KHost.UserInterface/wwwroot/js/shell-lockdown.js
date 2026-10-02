// Makes the native window behave like an appliance; loaded only outside Development.
// The web half only: a text field's own "Inspect Element" is the webview's to turn off.

(function () {
    const editableTypes = new Set([
        'text', 'search', 'email', 'password', 'number', 'tel', 'url', ''
    ]);

    // Where a menu is genuinely useful: cut, copy, paste and spelling in a field being typed into.
    function isTextEntry(element) {
        if (!element) return false;
        if (element.isContentEditable) return true;

        const tag = element.tagName;
        if (tag === 'TEXTAREA') return true;
        if (tag !== 'INPUT') return false;

        return !element.readOnly && !element.disabled
            && editableTypes.has((element.getAttribute('type') || 'text').toLowerCase());
    }

    document.addEventListener('contextmenu', function (event) {
        if (!isTextEntry(event.target)) event.preventDefault();
    });

    // Keys are browser-keys.js's, which every surface loads; this file is the pointer half.

    // Mouse thumb buttons are back and forward.
    for (const type of ['mousedown', 'mouseup', 'auxclick']) {
        document.addEventListener(type, function (event) {
            if (event.button === 3 || event.button === 4) stop(event);
        }, { capture: true });
    }

    // Two-finger swipe-to-navigate has no event of its own; keeping a state on the stack means the
    // gesture lands back where it started instead of leaving the console.
    history.pushState(null, '', location.href);
    window.addEventListener('popstate', function () {
        history.pushState(null, '', location.href);
    });

    function stop(event) {
        event.preventDefault();
        event.stopPropagation();
    }
})();
