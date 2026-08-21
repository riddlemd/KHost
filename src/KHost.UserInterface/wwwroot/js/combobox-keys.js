// Stops the browser acting on the keys a combobox needs while its menu is open — chiefly Enter,
// which a surrounding form would otherwise take as an implicit submit.
//
// Blazor cannot: @onkeydown:preventDefault is fixed at render time, so it could only suppress
// every key, typing included.

const CLAIMED = ['Enter', 'ArrowUp', 'ArrowDown', 'Escape'];

export function init(element) {
    const onKeyDown = e => {
        if (element.dataset.menuOpen === 'true' && CLAIMED.includes(e.key))
            e.preventDefault();
    };

    // On the element, so it runs before Blazor's delegated listener; cancelling the default
    // action still leaves Blazor's own handler to see the key.
    element.addEventListener('keydown', onKeyDown);

    return { dispose: () => element.removeEventListener('keydown', onKeyDown) };
}
