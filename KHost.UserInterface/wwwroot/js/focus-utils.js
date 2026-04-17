export function focusSingerInput() {
    const elm = document.getElementById('singer-name-input');

    if (!elm)
        return;

    elm.focus();
}

export function focusSingerSongQueue() {
    const elm = document.getElementById('panel-queue-detail');

    if (!elm)
        return;

    elm.focus();
}
