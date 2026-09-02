// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { LocalizationProvider } from './Localization';
import { en } from './en';
import { pl } from './pl';
import { storeLocale } from './locale';
import { useLocalization } from './useLocalization';

// What the browser or the operating system says the person reads, which a first run is resolved from. jsdom answers
// this from a property on `navigator`, so a test states one by writing it and puts back what was there afterwards.
const declaredLanguages = navigator.languages;

function preferring(languages: readonly string[]): void {
    Object.defineProperty(navigator, 'languages', { value: languages, configurable: true });
}

afterEach(() => {
    preferring(declaredLanguages);
    window.localStorage.clear();
    document.documentElement.removeAttribute('lang');
});

function Probe() {
    const { translate } = useLocalization();

    return <p>{translate('accounts.reading')}</p>;
}

function Filled({ values }: { readonly values: Readonly<Record<string, string>> }) {
    const { translate } = useLocalization();

    return <p>{translate('accounts.failed', values)}</p>;
}

describe('LocalizationProvider', () => {
    it('opens in the language the browser says is preferred', () => {
        preferring(['pl-PL', 'en']);

        render(
            <LocalizationProvider>
                <Probe />
            </LocalizationProvider>,
        );

        expect(screen.getByText(pl['accounts.reading'])).toBeDefined();
    });

    it('opens in English where the browser prefers a language the client does not carry', () => {
        preferring(['de-DE']);

        render(
            <LocalizationProvider>
                <Probe />
            </LocalizationProvider>,
        );

        expect(screen.getByText(en['accounts.reading'])).toBeDefined();
    });

    it('opens in the language explicitly chosen, over what the browser prefers', () => {
        preferring(['en-GB']);
        storeLocale('pl');

        render(
            <LocalizationProvider>
                <Probe />
            </LocalizationProvider>,
        );

        expect(screen.getByText(pl['accounts.reading'])).toBeDefined();
    });

    it('declares the active language on the document, which is what a screen reader pronounces by', () => {
        preferring(['pl']);

        render(
            <LocalizationProvider>
                <Probe />
            </LocalizationProvider>,
        );

        expect(document.documentElement.lang).toBe('pl');
    });

    it('fills the hole in a message from the value it was given', () => {
        preferring(['en']);

        render(
            <LocalizationProvider>
                <Filled values={{ reason: en['failure.unauthenticated'] }} />
            </LocalizationProvider>,
        );

        expect(screen.getByText('The accounts could not be read: unauthenticated.')).toBeDefined();
    });

    it('fills a hole into the sentence of the active language rather than into the English one', () => {
        preferring(['pl']);

        render(
            <LocalizationProvider>
                <Filled values={{ reason: pl['failure.unauthenticated'] }} />
            </LocalizationProvider>,
        );

        expect(screen.getByText('Nie udało się odczytać kont: brak uwierzytelnienia.')).toBeDefined();
    });

    it('leaves a hole nobody filled on the screen rather than a gap in the sentence', () => {
        preferring(['en']);

        render(
            <LocalizationProvider>
                <Filled values={{ unrelated: 'ignored' }} />
            </LocalizationProvider>,
        );

        expect(screen.getByText('The accounts could not be read: {reason}.')).toBeDefined();
    });
});
