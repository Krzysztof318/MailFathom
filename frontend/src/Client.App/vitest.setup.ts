// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// React Testing Library unmounts what a test rendered by itself only when the test framework's hooks are globals, and
// this suite imports them instead. Without this the document survives from one test to the next, so a query matching
// one element would match the last three renders of it and report an ambiguity rather than the assertion that failed.
afterEach(cleanup);

// Node publishes a Web Storage implementation of its own, and the jsdom window this suite runs in is the worker's
// global object — so Node's `localStorage` getter is the one on it, and it answers `undefined` unless the process was
// started with `--localstorage-file`. jsdom's own storage is there and reachable; nothing is being invented here, only
// the name put back on the object it belongs to, so a component reading storage in a test reads what a browser would
// give it rather than a global that reports a browser API as absent.
const jsdomStorage = (window as unknown as Record<string, unknown>)['_localStorage'];

if (jsdomStorage instanceof Storage) {
    Object.defineProperty(globalThis, 'localStorage', {
        value: jsdomStorage,
        configurable: true,
        writable: false,
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
