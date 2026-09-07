// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { localeNames, locales, readStoredLocale } from './localization/locale';
import { openSettings, renderApp, resetsBetweenTests } from './App.harness';

// The language chosen on the settings screen, and what remembering it owes the next run. The arrangement is
// `App.harness`, which the rest of this family shares.

resetsBetweenTests();

describe('App language', () => {
    it('offers each language under its own name, and no other', () => {
        renderApp();
        openSettings();

        const offered = within(screen.getByRole('group', { name: 'Language' })).getAllByRole('radio');

        expect(offered.map((choice) => choice.closest('label')?.textContent)).toEqual(
            locales.map((locale) => localeNames[locale]),
        );
    });

    it('rewrites the screen when another language is chosen, without anything being restarted', async () => {
        renderApp();
        await screen.findByRole('heading', { name: 'Discover', level: 1 });
        openSettings();

        fireEvent.click(screen.getByRole('radio', { name: localeNames.pl }));

        expect(screen.getByRole('heading', { name: 'Odkrywaj', level: 1 })).toBeDefined();
        expect(document.documentElement.lang).toBe('pl');
    });

    it('remembers the choice, so a later run of either head opens in it', () => {
        renderApp();
        openSettings();

        fireEvent.click(screen.getByRole('radio', { name: localeNames.pl }));

        expect(readStoredLocale()).toBe('pl');
    });
});
