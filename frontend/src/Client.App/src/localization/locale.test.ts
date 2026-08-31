// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { en } from './en';
import {
    catalogues,
    defaultLocale,
    localeNames,
    locales,
    narrowToOfferedLocale,
    readStoredLocale,
    storeLocale,
} from './locale';

afterEach(() => {
    window.localStorage.clear();
});

describe('locales', () => {
    it('offers exactly the languages a catalogue and a name were written for', () => {
        expect(Object.keys(catalogues).sort()).toEqual([...locales].sort());
        expect(Object.keys(localeNames).sort()).toEqual([...locales].sort());
    });
});

describe('catalogues', () => {
    // English declares the keys and every other language is typed against it, so a key present in one and missing from
    // the other does not compile. This is the same statement made where a reader of the suite meets it, and it is what
    // fails if that annotation is ever loosened.
    it.each(locales)('carries in %s every key English declares, and no other', (locale) => {
        expect(Object.keys(catalogues[locale]).sort()).toEqual(Object.keys(en).sort());
    });

    it.each(locales)('leaves no message in %s empty, which would fall back to English on the screen', (locale) => {
        expect(Object.values(catalogues[locale]).filter((message) => message === '')).toEqual([]);
    });
});

describe('narrowToOfferedLocale', () => {
    it('reads a preference that names an offered language', () => {
        expect(narrowToOfferedLocale(['pl'])).toBe('pl');
    });

    it('reads a regional preference by its language, no region being carried', () => {
        expect(narrowToOfferedLocale(['pl-PL'])).toBe('pl');
    });

    it('takes the first offered language rather than the first preference', () => {
        expect(narrowToOfferedLocale(['de-DE', 'fr', 'pl-PL', 'en'])).toBe('pl');
    });

    it('resolves a language the client does not carry to English', () => {
        expect(narrowToOfferedLocale(['de-DE'])).toBe(defaultLocale);
    });

    it('resolves an empty preference list to English', () => {
        expect(narrowToOfferedLocale([])).toBe(defaultLocale);
    });
});

describe('readStoredLocale', () => {
    it('reads back the language that was chosen', () => {
        storeLocale('pl');

        expect(readStoredLocale()).toBe('pl');
    });

    it('reads no language where none was chosen', () => {
        expect(readStoredLocale()).toBeNull();
    });

    it('refuses a stored value naming a language the client does not carry', () => {
        window.localStorage.setItem('mailfathom.locale', 'de');

        expect(readStoredLocale()).toBeNull();
    });
});
