// Stops the browser acting on a key a combobox needs while its menu is open, e.g. Enter's implicit submit.
// Blazor's @onkeydown:preventDefault can't: fixed at render time, it would suppress typing too.

const NAVIGATION = ['ArrowUp', 'ArrowDown', 'Escape'];

export function init(element) {
    const onKeyDown = e => {
        if (element.dataset.menuOpen !== 'true') return;

        // Enter is only ours while there is a row to choose. With no matches the surrounding form
        // should have it, so a name that matches nobody can still be submitted.
        const claimed = e.key === 'Enter'
            ? element.dataset.hasRows === 'true'
            : NAVIGATION.includes(e.key);

        if (claimed) e.preventDefault();
    };

    // On the element, so it runs before Blazor's delegated listener; cancelling the default
    // action still leaves Blazor's own handler to see the key.
    element.addEventListener('keydown', onKeyDown);

    return { dispose: () => element.removeEventListener('keydown', onKeyDown) };
}
