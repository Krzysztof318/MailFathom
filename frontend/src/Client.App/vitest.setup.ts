// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { cleanup, configure } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';

// How long `findBy*` and `waitFor` may wait for the condition they were given. React Testing Library's own default is
// one second, which is not a budget this suite can meet: a test that mounts the whole application over a fake
// deployment waits on a screen that arrives after two answers and several commits, and one second of that is what a
// machine running at four times its cores spends before the first of them. So the wait expired rather than the
// condition failing, and the report — `Unable to find an element with the text` — read as the client no longer drawing
// what the test asked for.
//
// It is a ceiling rather than a duration anything sleeps for: every wait here still ends the moment its condition
// holds, and nothing in this suite is slower for the number being larger. What it bounds is how long a test waits
// before saying the screen never arrived, and `frontend/vitest.config.ts` bounds the test around it.
configure({ asyncUtilTimeout: 5_000 });

// React Testing Library unmounts what a test rendered by itself only when the test framework's hooks are globals, and
// this suite imports them instead. Without this the document survives from one test to the next, so a query matching
// one element would match the last three renders of it and report an ambiguity rather than the assertion that failed.
afterEach(cleanup);

// Both stores emptied in front of every test rather than only behind it. A clear on the way out has to be ordered
// against everything that can still write after it — the unmount above, a read that resolves while the teardown hooks
// are running, an effect that commits with either — and it cannot be, so what it leaves behind reaches the next test
// as something that test never asked for. `Composer.test.tsx` is where that was found: a message another test had
// been writing was restored into a composer that then refused to close without asking, which is a screen the client
// draws correctly for a state the suite invented. Emptying on the way in orders against nothing, because whatever the
// last test left, this one has not started yet.
beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
});

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
// inside it is readable, and closing unmarks it, records the answer it was closed with, and fires the `close` event a
// component listens for. The answer is part of it rather than an extra — the platform's close algorithm sets
// `returnValue` from the argument, and a component that reads which button was pressed reads it there, so a stand-in
// that dropped it would report every answer as the same one. What is deliberately *not* here is everything a modal
// actually is — the top layer, the backdrop, the focus trap, and Escape — because none of that is this application's
// code, and a suite that reimplemented it would be asserting the reimplementation. Those belong to the browser suite,
// which has a browser.
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
        value(this: HTMLDialogElement, returnValue?: string) {
            this.open = false;

            // What a component reads to tell which of a dialog's controls closed it, which is the platform's own way
            // of answering that and therefore the part of `close` worth putting back with it.
            if (returnValue !== undefined) {
                this.returnValue = returnValue;
            }

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
