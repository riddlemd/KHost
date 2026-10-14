// Loads the screen page's classic scripts into one vm context, as the page joins them: globals,
// defined in index.html's tag order. Program.cs inlines each tag where it sits, so that order is the
// page's; the csproj's EmbeddedResource list says which file each tag names.

'use strict';

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const projectDir = path.resolve(__dirname, '..');

/// Script names in the order index.html's tags put them.
function scriptOrder() {
    const html = fs.readFileSync(path.join(projectDir, 'screen-ui', 'index.html'), 'utf8');
    return [...html.matchAll(/<script src="([^"]+)"><\/script>/g)].map((m) => m[1]);
}

/// Script name → file on disk, from the csproj's LogicalNames (browser-keys.js lives in the UI project).
function scriptSources() {
    const csproj = fs.readFileSync(path.join(projectDir, 'KHost.LocalScreen.csproj'), 'utf8');
    const sources = new Map();
    for (const m of csproj.matchAll(/<EmbeddedResource Include="([^"]+)" LogicalName="screen-ui\/([^"]+)"/g)) {
        sources.set(m[2], path.join(projectDir, ...m[1].split('\\')));
    }
    return sources;
}

/// An element that takes listeners and remembers them, enough for title-bar.js and the overlay.
function stubElement(extra = {}) {
    const listeners = new Map();
    return {
        dataset: {},
        hidden: false,
        listeners,
        addEventListener(type, fn) { if (!listeners.has(type)) listeners.set(type, []); listeners.get(type).push(fn); },
        removeEventListener(type, fn) { const list = listeners.get(type) || []; const i = list.indexOf(fn); if (i >= 0) list.splice(i, 1); },
        dispatch(type, event) { for (const fn of [...(listeners.get(type) || [])]) fn(event); },
        setAttribute(name, value) { this[`attr:${name}`] = value; },
        getAttribute(name) { return this[`attr:${name}`] ?? null; },
        querySelector() { return null; },
        querySelectorAll() { return []; },
        closest() { return null; },
        ...extra,
    };
}

/// A 2D context whose every method is a no-op, and measureText answers a width.
function stubContext2d() {
    return new Proxy({}, {
        get(target, key) {
            if (key in target) return target[key];
            if (key === 'measureText') return (text) => ({ width: String(text).length * 10 });
            return () => {};
        },
        set(target, key, value) { target[key] = value; return true; },
    });
}

function stubCanvas() {
    return stubElement({ width: 0, height: 0, clientWidth: 640, clientHeight: 360, getContext: () => stubContext2d() });
}

/// A fresh context per call: no test sees another's globals. player.js is left out by default,
/// since its top level wires the whole page (see the README section in the spike report).
function loadScreen({ exclude = ['player.js'] } = {}) {
    const sources = scriptSources();
    const document = stubElement({
        documentElement: stubElement(),
        getElementById: () => null,
    });

    const sandbox = {
        console,
        document,
        navigator: { platform: 'test', userAgent: 'node' },
        performance: { now: () => 0 },
        setTimeout,
        clearTimeout,
        setInterval,
        clearInterval,
        requestAnimationFrame: () => 0,
        cancelAnimationFrame: () => {},
        devicePixelRatio: 1,
    };
    const context = vm.createContext(sandbox);
    vm.runInContext('globalThis.window = globalThis; window.addEventListener = () => {};', context);

    for (const name of scriptOrder()) {
        if (exclude.includes(name)) continue;
        const file = sources.get(name);
        if (!file) throw new Error(`index.html names ${name}, which the csproj does not embed`);
        vm.runInContext(fs.readFileSync(file, 'utf8'), context, { filename: file });
    }

    // Top-level const/let are not properties of the global, so read them through the context.
    context.$ = (expression) => vm.runInContext(expression, context);
    return context;
}

/// A value made inside the context, as plain data: deepStrictEqual compares prototypes, and each
/// realm has its own Object.prototype.
function plain(value) {
    return JSON.parse(JSON.stringify(value));
}

module.exports = { loadScreen, plain, scriptOrder, scriptSources, stubElement, stubCanvas, stubContext2d };
