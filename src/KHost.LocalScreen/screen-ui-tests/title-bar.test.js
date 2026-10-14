'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadScreen, plain, stubElement } = require('./load-screen.js');

function titleBarPage() {
    const screen = loadScreen();
    const maximise = stubElement();
    const bar = stubElement({ querySelector: () => maximise, querySelectorAll: () => [] });
    const doc = stubElement({
        documentElement: stubElement(),
        getElementById: (id) => (id === 'titlebar' ? bar : null),
    });
    const sent = [];
    screen.createTitleBar({
        doc,
        send: (message) => sent.push(plain(message)),
        requestFrame: () => {},
        setTimer: () => 0,
        clearTimer: () => {},
    });
    return { bar, doc, sent };
}

function dblclickOn(target) {
    return { target: { closest: (selector) => (selector === 'button' ? target : null) }, stopPropagation() {} };
}

test('createTitleBar_DoubleClick_MaximisesFromTheStripButNotFromItsButtons', () => {
    const { bar, sent } = titleBarPage();

    bar.dispatch('dblclick', dblclickOn({ tagName: 'BUTTON' }));
    assert.deepEqual(sent, [], 'a button is pressed, not the strip');

    bar.dispatch('dblclick', dblclickOn(null));
    assert.deepEqual(sent, [{ type: 'window-maximise' }]);
});

test('createTitleBar_DragStartAnywhere_IsDefaultPrevented', () => {
    const { doc } = titleBarPage();
    let prevented = false;

    doc.dispatch('dragstart', { preventDefault: () => { prevented = true; } });

    assert.equal(prevented, true);
});
