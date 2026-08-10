document.addEventListener('selectstart', (e) => {
    if (e.target.closest('input, textarea, [contenteditable]')) return;
    e.preventDefault();
});
