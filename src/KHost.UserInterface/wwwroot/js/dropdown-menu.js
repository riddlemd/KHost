// Menus are position:fixed so an overflow:hidden ancestor can't clip them, which means they
// get no layout from the DOM and must be placed against their trigger here.
export function positionMenu(anchorEl, menuEl) {
    const rect = anchorEl.getBoundingClientRect();

    menuEl.style.top = `${rect.bottom + 4}px`;
    menuEl.style.right = `${window.innerWidth - rect.right}px`;
    menuEl.style.minWidth = `${rect.width}px`;
}
