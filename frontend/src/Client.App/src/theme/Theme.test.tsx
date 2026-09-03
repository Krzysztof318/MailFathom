// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ThemeChoice } from '../shell/Preferences';
import { ThemeProvider } from './Theme';
import { readStoredThemeChoice } from './themeChoice';

// What the machine itself is set to. jsdom answers every media query with `matches: false` and never changes its
// answer, so a test that states a machine preference — or changes one while the client is open — defines the query
// itself and puts back what was there afterwards, the way `Localization.test.tsx` states a language preference.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

let listeners: (() => void)[] = [];
let machineIsDark = false;

function machineTurns(dark: boolean): void {
    machineIsDark = dark;
    act(() => {
        for (const listener of listeners) {
            listener();
        }
    });
}

beforeEach(() => {
    listeners = [];
    machineIsDark = false;

    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: machineIsDark && query.includes('dark'),
            addEventListener: (_: string, listener: () => void) => {
                listeners.push(listener);
            },
            removeEventListener: (_: string, listener: () => void) => {
                listeners = listeners.filter((listening) => listening !== listener);
            },
        }),
    });
});

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }

    window.localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
});

// The theme reaches a screen as one attribute on the document, which every semantic token in `styles.css` is declared
// against twice. It is what a person sees, as closely as a suite that computes no styles can ask about it.
function paintedTheme(): string | undefined {
    return document.documentElement.dataset['theme'];
}

function renderChoice(): void {
    render(
        <LocalizationProvider>
            <ThemeProvider>
                <ThemeChoice />
            </ThemeProvider>
        </LocalizationProvider>,
    );
}

describe('ThemeProvider', () => {
    it('paints light where nothing was chosen and the machine is light', () => {
        renderChoice();

        expect(paintedTheme()).toBe('light');
    });

    it('paints dark where nothing was chosen and the machine is dark', () => {
        machineIsDark = true;

        renderChoice();

        expect(paintedTheme()).toBe('dark');
    });

    it('repaints when the machine changes while the client is open', () => {
        renderChoice();

        machineTurns(true);

        expect(paintedTheme()).toBe('dark');
    });

    it('leaves an explicit choice alone when the machine changes under it', () => {
        renderChoice();
        fireEvent.click(screen.getByRole('radio', { name: 'Light' }));

        machineTurns(true);

        expect(paintedTheme()).toBe('light');
    });
});

describe('useTheme', () => {
    it('refuses to answer outside the provider rather than painting a theme of its own', () => {
        expect(() => {
            render(
                <LocalizationProvider>
                    <ThemeChoice />
                </LocalizationProvider>,
            );
        }).toThrow(/ThemeProvider/);
    });
});

describe('ThemeChoice', () => {
    it('offers following the machine beside each of the two themes, as one group of choices', () => {
        renderChoice();

        expect(screen.getAllByRole('radio').map((offered) => offered.parentElement?.textContent)).toEqual([
            'Auto',
            'Light',
            'Dark',
        ]);
    });

    it('repaints the client when another theme is chosen, without anything being restarted', () => {
        renderChoice();

        fireEvent.click(screen.getByRole('radio', { name: 'Dark' }));

        expect(paintedTheme()).toBe('dark');
    });

    it('remembers the choice, so a later run of either head opens in it', () => {
        renderChoice();

        fireEvent.click(screen.getByRole('radio', { name: 'Dark' }));

        expect(readStoredThemeChoice()).toBe('dark');
    });
});
