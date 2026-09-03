// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { deviceKeys, deviceStore } from '../device/deviceStore';
import { en, type Catalogue } from './en';
import { pl } from './pl';

// Which language was chosen is one of the things the device holds rather than the deployment, so it is read and written
// through `device/deviceStore.ts` — including the handling a browser or a WebView refusing storage needs, which is that
// module's rather than this one's. The choice survives a restart of either head because both store it in the same
// place: the web bundle in the browser's origin storage, the desktop shell in its WebView's.

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
    const stored = deviceStore().read(deviceKeys.locale);

    return isOfferedLocale(stored) ? stored : null;
}

export function storeLocale(locale: Locale): void {
    deviceStore().write(deviceKeys.locale, locale);
}

/** What the client opens in: the explicit choice, else the browser or operating-system preference, else English. */
export function preferredLocale(): Locale {
    return readStoredLocale() ?? narrowToOfferedLocale(navigator.languages);
}
