// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { en, type Catalogue } from './en';
import { pl } from './pl';

/** The languages the client is offered in. Neutral rather than regional: nothing differs between regions yet. */
export const locales = ['en', 'pl'] as const;

export type Locale = (typeof locales)[number];

/** What a first run resolves to when nothing it reads names a language the client carries. */
export const defaultLocale: Locale = 'en';

export const catalogues: Readonly<Record<Locale, Catalogue>> = { en, pl };

// A language is named in its own language wherever it is chosen, which is why these are not catalogue entries: somebody
// who has landed in a language they do not read has to recognise their own in the list, and translating the list would
// be the one place that fails them.
export const localeNames: Readonly<Record<Locale, string>> = { en: 'English', pl: 'Polski' };

// What the explicit choice is written under. It survives a restart of either head because both store it in the same
// place: the web bundle in the browser's origin storage, the desktop shell in its WebView's.
//
// Reached as `window.localStorage` rather than as the bare global on purpose. Node publishes a `localStorage` global of
// its own, which is unavailable unless the process was started with `--localstorage-file`, and it wins over the one the
// test environment's document carries — so the bare name is the runtime's under Vitest and the document's in a browser,
// which is two different objects behind one identifier.
const storageKey = 'mailfathom.locale';

export function isOfferedLocale(value: unknown): value is Locale {
    return typeof value === 'string' && (locales as readonly string[]).includes(value);
}

/**
 * The first offered language among the preferences, in the order they were given, falling back to {@link defaultLocale}.
 * A preference is matched on its language subtag alone, so `pl-PL` and `en-GB` resolve while nothing regional is
 * carried.
 */
export function narrowToOfferedLocale(preferences: readonly string[]): Locale {
    for (const preference of preferences) {
        const language = preference.split('-')[0]?.toLowerCase();
        if (isOfferedLocale(language)) {
            return language;
        }
    }

    return defaultLocale;
}

/** The language explicitly chosen on this machine, or `null` where none was chosen or what was stored is not offered. */
export function readStoredLocale(): Locale | null {
    try {
        const stored = window.localStorage.getItem(storageKey);
        return isOfferedLocale(stored) ? stored : null;
    } catch {
        return null;
    }
}

export function storeLocale(locale: Locale): void {
    try {
        window.localStorage.setItem(storageKey, locale);
    } catch {
        // A browser configured to refuse storage still runs the client; the choice then lasts the session rather than
        // outliving it, which is a smaller loss than a screen that fails to mount over a preference.
    }
}

/** What the client opens in: the explicit choice, else the browser or operating-system preference, else English. */
export function preferredLocale(): Locale {
    return readStoredLocale() ?? narrowToOfferedLocale(navigator.languages);
}
