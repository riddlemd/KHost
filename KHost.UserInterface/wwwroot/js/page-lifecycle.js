window.onPageOpen = function() {
    const input = document.querySelector('input:not([type="hidden"]):not([disabled])');
    input?.focus();
};
