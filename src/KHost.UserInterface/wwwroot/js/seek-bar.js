// Where along a bar a click landed, as a fraction of its width.
//
// Blazor's MouseEventArgs cannot answer this: OffsetX is relative to whatever was clicked, which
// for a progress bar is usually the fill sitting on top of the track, and the element's width is
// not in the event at all. clientX against the track's own rect is independent of both.

export function fractionFromClick(trackEl, clientX) {
    const rect = trackEl.getBoundingClientRect();
    if (rect.width <= 0) return 0;

    return Math.min(1, Math.max(0, (clientX - rect.left) / rect.width));
}
