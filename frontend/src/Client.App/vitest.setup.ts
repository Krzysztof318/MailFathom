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
