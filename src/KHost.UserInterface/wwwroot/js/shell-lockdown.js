// Makes the native window behave like an appliance rather than a browser. Loaded only when KHost
// is running in its own window and not in Development — a browser tab keeps its normal behaviour,
// and a developer keeps their tools.
//
// This is the web half. It cannot stop anything the host webview offers outside the page, notably
// the "Inspect Element" item in a text field's own menu; that has to be turned off on the webview.

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

    // Reload throws away the Blazor circuit mid-show; back leaves the console entirely.
    const blockedKeys = new Set(['F5', 'F12']);

    document.addEventListener('keydown', function (event) {
        const key = event.key;
        const lower = typeof key === 'string' ? key.toLowerCase() : '';
        const accel = event.ctrlKey || event.metaKey;

        if (blockedKeys.has(key)) return stop(event);

        // Reload: Ctrl/Cmd+R, and the hard variants.
        if (accel && lower === 'r') return stop(event);

        // Developer tools: Cmd+Opt+I/J/C on macOS, Ctrl+Shift+I/J/C elsewhere.
        if (accel && (event.altKey || event.shiftKey) && ['i', 'j', 'c'].includes(lower)) return stop(event);

        // Page source: Ctrl/Cmd+U.
        if (accel && lower === 'u') return stop(event);

        // Back and forward: Cmd+[ / Cmd+] and Cmd+Arrow on macOS, Alt+Arrow elsewhere.
        if (accel && (key === '[' || key === ']')) return stop(event);
        if ((accel || event.altKey) && (key === 'ArrowLeft' || key === 'ArrowRight')) return stop(event);

        // Backspace navigates back in some hosts unless it is being typed into something.
        if (key === 'Backspace' && !isTextEntry(event.target)) return stop(event);
    }, { capture: true });

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
