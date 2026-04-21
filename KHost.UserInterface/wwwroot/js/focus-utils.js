export function focusSingerInput() {
    const elm = document.querySelector('.kh-singer-queue__name-input');

    if (!elm)
        return;

    elm.focus();
}

export function focusSingerDetail() {
    const elm = document.querySelector('.kh-app__singer-detail');

    if (!elm)
        return;

    elm.focus();
}
