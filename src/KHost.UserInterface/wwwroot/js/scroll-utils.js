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
