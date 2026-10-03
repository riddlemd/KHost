window.scrollIntoViewSmooth = function(selector) {
    const element = document.querySelector(selector);
    if (element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
};

window.focusFirstInput = function(container) {
    const el = container.querySelector('[autofocus]')
        ?? container.querySelector('input:not([type=hidden]):not([readonly]), textarea, select');
    if (el) el.focus();
};

// A closed dialog leaves the DOM with focus inside it, which drops the keyboard on <body>: a host who
// removed a singer with Delete would need Ctrl+3 again before the next key did anything. So hand
// focus back to whatever held it before the first dialog opened, unless something has taken it since.
(function () {
    let lastOutside = null;
    let dialogWasOpen = false;

    document.addEventListener('focusin', function (event) {
        if (!(event.target instanceof Element) || !event.target.closest('.kh-scrim'))
            lastOutside = event.target;
    });

    // Acts only as the last dialog goes. Every Blazor render lands here, and focus on <body> at any
    // other moment is the host's own click on empty space, which is not to be undone.
    new MutationObserver(function () {
        const open = document.querySelector('.kh-scrim') !== null;
        const closing = dialogWasOpen && !open;
        dialogWasOpen = open;
        if (!closing) return;

        const active = document.activeElement;
        if (active && active !== document.body) return;

        if (lastOutside && lastOutside.isConnected && typeof lastOutside.focus === 'function')
            lastOutside.focus();
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
