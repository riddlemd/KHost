// Where along a bar a click landed, as a fraction of its width.
// Blazor's MouseEventArgs can't answer this: OffsetX is relative to whatever was clicked.

export function fractionFromClick(trackEl, clientX) {
    const rect = trackEl.getBoundingClientRect();
    if (rect.width <= 0) return 0;

    return Math.min(1, Math.max(0, (clientX - rect.left) / rect.width));
}
