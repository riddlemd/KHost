export function positionMenu(toggleEl, menuEl) {
    const btnRect = toggleEl.closest('.kh-split-btn').getBoundingClientRect();
    menuEl.style.top = `${btnRect.bottom + 4}px`;
    menuEl.style.right = `${window.innerWidth - btnRect.right}px`;
    menuEl.style.minWidth = `${btnRect.width}px`;
}
