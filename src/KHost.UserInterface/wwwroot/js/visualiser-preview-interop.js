// Hands the Visualisations page's preview frame the entry to draw. The frame is sandboxed with no
// origin, so messages go to '*'; they carry only a preset and its look.
window.khVisualiserPreview = (() => {
    const last = new WeakMap();

    function post(frame) {
        const message = last.get(frame);
        if (message && frame.contentWindow) frame.contentWindow.postMessage(message, '*');
    }

    // A frame that loads after the page posted asks again, and gets whatever it missed.
    window.addEventListener('message', (event) => {
        if (!event.data || event.data.type !== 'visualiser-preview-ready') return;
        document.querySelectorAll('iframe[data-kh-visualiser-preview]').forEach((frame) => {
            if (frame.contentWindow === event.source) post(frame);
        });
    });

    return {
        show(frame, entry) {
            if (!frame) return;
            last.set(frame, Object.assign({ type: 'visualisation' }, entry));
            post(frame);
        },
    };
})();
