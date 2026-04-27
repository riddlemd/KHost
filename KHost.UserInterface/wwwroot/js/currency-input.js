export function init(element) {
    element.addEventListener('input', onInput);
    element.addEventListener('focus', onFocus);
    return {
        dispose: () => {
            element.removeEventListener('input', onInput);
            element.removeEventListener('focus', onFocus);
        }
    };
}

export function setValue(element, cents) {
    element.value = '$' + (cents / 100).toFixed(2);
}

function onInput(e) {
    const digits = e.target.value.replace(/[^\d]/g, '');
    const cents = parseInt(digits || '0', 10);
    e.target.value = '$' + (cents / 100).toFixed(2);
}

function onFocus(e) {
    e.target.select();
}
