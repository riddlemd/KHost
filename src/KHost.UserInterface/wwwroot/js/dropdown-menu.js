// Menus are position:fixed so an overflow:hidden ancestor can't clip them, which means they
// get no layout from the DOM and must be placed against their trigger here.

/// Space between the trigger and the menu, and between the menu and the bottom of the window.
const Gap = 4;
const Margin = 8;

/// Below this a menu is not worth showing in place; it would be a scroll bar with a row in it.
const MinHeight = 120;

export function positionMenu(anchorEl, menuEl) {
    const rect = anchorEl.getBoundingClientRect();

    const below = window.innerHeight - rect.bottom - Gap - Margin;
    const above = rect.top - Gap - Margin;

    // Opens upward when the room below can't hold MinHeight. Fixed positioning means nothing
    // scrolls a menu back into view, so a trigger near the bottom would strand its own last item.
    const flip = below < MinHeight && above > below;

    // Bound to the room on whichever side it opens, so a long menu scrolls itself rather than
    // running off the edge, and never taller than the window whichever side that is.
    menuEl.style.maxHeight =
        `${Math.min(window.innerHeight - 2 * Margin, Math.max(MinHeight, flip ? above : below))}px`;

    menuEl.style.right = `${window.innerWidth - rect.right}px`;
    menuEl.style.minWidth = `${rect.width}px`;

    // Anchored by the edge it grows from, and the opposite one cleared: the element is reused
    // across opens, so a stale top would fight the bottom that replaces it.
    if (flip) {
        menuEl.style.top = 'auto';
        menuEl.style.bottom = `${window.innerHeight - rect.top + Gap}px`;
    } else {
        menuEl.style.bottom = 'auto';
        menuEl.style.top = `${rect.bottom + Gap}px`;
    }
}

// A panel beside a row of an open menu, rather than inside it. Same reasoning as above: fixed, so
// it needs placing here, and bounded, so a long list scrolls itself instead of running off-screen.
export function positionFlyout(rowEl, panelEl) {
    const row = rowEl.getBoundingClientRect();

    // Capped before measuring: the height decides where the top can sit.
    panelEl.style.maxHeight = `${window.innerHeight - 2 * Margin}px`;

    const panel = panelEl.getBoundingClientRect();

    // Opens to the left. The menus this belongs to hang off the right of the header, so the space
    // on the far side is the space there is, but fall back rather than run off the edge.
    let left = row.left - panel.width - Gap;
    if (left < Margin)
        left = Math.min(row.right + Gap, window.innerWidth - panel.width - Margin);

    const top = Math.min(row.top, window.innerHeight - panel.height - Margin);

    panelEl.style.left = `${Math.max(Margin, left)}px`;
    panelEl.style.top = `${Math.max(Margin, top)}px`;
}
