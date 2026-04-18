export function focusSingerInput() {
    const elm = document.getElementById('singer-name-input');

    if (!elm)
        return;

    elm.focus();
}

export function focusSingerDetail() {
    const elm = document.getElementById('panel-singer-detail');

    if (!elm)
        return;

    elm.focus();
}
