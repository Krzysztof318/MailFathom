// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// React Testing Library unmounts what a test rendered by itself only when the test framework's hooks are globals, and
// this suite imports them instead. Without this the document survives from one test to the next, so a query matching
// one element would match the last three renders of it and report an ambiguity rather than the assertion that failed.
afterEach(cleanup);

// Node publishes a Web Storage implementation of its own, and the jsdom window this suite runs in is the worker's
// global object — so Node's getters are the ones on it: `localStorage` answers `undefined` unless the process was
// started with `--localstorage-file`, and `sessionStorage` answers a store belonging to the worker rather than to the
// document. jsdom's own two are there and reachable; nothing is being invented here, only the names put back on the
// object they belong to, so a component reading storage in a test reads what a browser would give it rather than a
// global that reports a browser API as absent or hands out one shared between files.
reinstateJsdomStorage('localStorage', '_localStorage');
reinstateJsdomStorage('sessionStorage', '_sessionStorage');

function reinstateJsdomStorage(name: string, jsdomName: string): void {
    const jsdomStorage = (window as unknown as Record<string, unknown>)[jsdomName];

    if (jsdomStorage instanceof Storage) {
        Object.defineProperty(globalThis, name, {
            value: jsdomStorage,
            configurable: true,
            writable: false,
        });
    }
}

// jsdom implements no part of the popover API: not the invoker attribute, and not `showPopover` or `hidePopover`. A
// component that folds its own popover away while something in front of it is open therefore calls a method that is
// not there, and the whole test file fails on that rather than on what it was asserting. What is put back is the pair
// as the platform's own no-op for a popover that is already in the state asked for — nothing here can observe one
// opening, jsdom drawing every popover closed, so what a test may assert about them stays what the markup declares
// and never what pressing one did. Anything more would be a second implementation of the platform inside the suite.
for (const method of ['showPopover', 'hidePopover'] as const) {
    if (typeof HTMLElement.prototype[method] !== 'function') {
        Object.defineProperty(HTMLElement.prototype, method, {
            configurable: true,
            writable: true,
            value: () => undefined,
        });
    }
}

// jsdom implements the `dialog` element but neither of the two methods a modal one is driven by. What is put back is
// the part of them a document can have: opening marks the element open, so it is exposed as a dialog and what is
// inside it is readable, and closing unmarks it and fires the `close` event a component listens for. What is
// deliberately *not* here is everything a modal actually is — the top layer, the backdrop, the focus trap, and Escape —
// because none of that is this application's code, and a suite that reimplemented it would be asserting the
// reimplementation. Those belong to the browser suite, which has a browser.
if (typeof HTMLDialogElement.prototype.showModal !== 'function') {
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
        configurable: true,
        writable: true,
        value(this: HTMLDialogElement) {
            this.open = true;
        },
    });
}

if (typeof HTMLDialogElement.prototype.close !== 'function') {
    Object.defineProperty(HTMLDialogElement.prototype, 'close', {
        configurable: true,
        writable: true,
        value(this: HTMLDialogElement) {
            this.open = false;
            this.dispatchEvent(new Event('close'));
        },
    });
}

// jsdom evaluates no media query and publishes no `matchMedia` at all, so a component asking what appearance the
// machine is set to fails on a missing function rather than reading a preference. What is put back answers the way a
// browser whose machine matches nothing does, and never changes its answer — this environment has no machine
// preference to report and computes no styles. A test that states one defines its own over this, the way
// `src/theme/Theme.test.tsx` does, which is the same shape `Localization.test.tsx` states a language preference in.
if (typeof window.matchMedia !== 'function') {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: false,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}
