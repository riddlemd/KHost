window.scrollIntoViewSmooth = function(selector) {
    const element = document.querySelector(selector);
    if (element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
};
