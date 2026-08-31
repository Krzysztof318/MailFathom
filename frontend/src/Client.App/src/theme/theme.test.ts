// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { isThemeChoice, preferredThemeChoice, readStoredThemeChoice, storeThemeChoice, themeChoices } from './theme';

afterEach(() => {
    window.localStorage.clear();
});

describe('isThemeChoice', () => {
    it.each(themeChoices)('recognizes %s', (choice) => {
        expect(isThemeChoice(choice)).toBe(true);
    });

    it('refuses a value that is not one of the three, whatever its type', () => {
        expect(isThemeChoice('contrast')).toBe(false);
        expect(isThemeChoice(null)).toBe(false);
    });
});

describe('readStoredThemeChoice', () => {
    it('reads back what was stored, which is how a later run of either head opens in it', () => {
        storeThemeChoice('dark');

        expect(readStoredThemeChoice()).toBe('dark');
    });

    it('reads nothing where a value the client does not offer was left behind', () => {
        window.localStorage.setItem('mailfathom.theme', 'contrast');

        expect(readStoredThemeChoice()).toBeNull();
    });
});

describe('preferredThemeChoice', () => {
    it('follows the machine where nothing was chosen', () => {
        expect(preferredThemeChoice()).toBe('system');
    });

    it('takes the explicit choice over following the machine', () => {
        storeThemeChoice('light');

        expect(preferredThemeChoice()).toBe('light');
    });
});
