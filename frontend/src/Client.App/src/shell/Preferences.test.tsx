// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ThemeProvider } from '../theme/Theme';
import { LanguageSegments, TabModeSwitch, ThemeSegments } from './Preferences';

// The width the tab mode needs is the one thing here jsdom answers for, and the suite's own setup answers `false` to
// every query — so a test about the wide case states the width it is about rather than inheriting one.
function atTabWidth(wideEnough: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: wideEnough,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function renderControl(control: ReactNode): void {
    render(
        <LocalizationProvider>
            <ThemeProvider>{control}</ThemeProvider>
        </LocalizationProvider>,
    );
}

describe('TabModeSwitch', () => {
    afterEach(() => {
        atTabWidth(false);
        window.localStorage.clear();
    });

    it('reports as a switch rather than as a checkbox', () => {
        atTabWidth(true);
        renderControl(<TabModeSwitch on={false} onChange={() => undefined} />);

        expect(screen.getByRole('switch', { name: /Tab mode/u })).toBeDefined();
    });

    it('is off unless the person turned it on', () => {
        atTabWidth(true);
        renderControl(<TabModeSwitch on={false} onChange={() => undefined} />);

        expect(screen.getByRole('switch', { name: /Tab mode/u })).toHaveProperty('checked', false);
    });

    it('is on where they did', () => {
        atTabWidth(true);
        renderControl(<TabModeSwitch on onChange={() => undefined} />);

        expect(screen.getByRole('switch', { name: /Tab mode/u })).toHaveProperty('checked', true);
    });

    it('reports a change rather than deciding anything itself', () => {
        atTabWidth(true);
        const onChange = vi.fn();
        renderControl(<TabModeSwitch on={false} onChange={onChange} />);

        fireEvent.click(screen.getByRole('switch', { name: /Tab mode/u }));

        expect(onChange).toHaveBeenCalledWith(true);
    });

    it('is inert below the width the tab mode needs, and says so in its own line', () => {
        atTabWidth(false);
        renderControl(<TabModeSwitch on={false} onChange={() => undefined} />);

        expect(screen.getByRole('switch', { name: /Tab mode/u })).toHaveProperty('disabled', true);
        expect(screen.getByText('available on a wider screen')).toBeDefined();
    });

    it('says so in the other interface language too', () => {
        atTabWidth(false);
        window.localStorage.setItem('mailfathom.locale', 'pl');
        renderControl(<TabModeSwitch on={false} onChange={() => undefined} />);

        expect(screen.getByText('dostępne na szerszym ekranie')).toBeDefined();
    });

    it('says nothing about the width where there is room for the tab mode', () => {
        atTabWidth(true);
        renderControl(<TabModeSwitch on={false} onChange={() => undefined} />);

        expect(screen.queryByText('available on a wider screen')).toBeNull();
    });
});

describe('ThemeSegments', () => {
    afterEach(() => {
        window.localStorage.clear();
    });

    it('offers the three choices as one named group', () => {
        renderControl(<ThemeSegments onChoose={() => undefined} />);

        expect(screen.getByRole('group', { name: 'Theme' })).toBeDefined();
        expect(screen.getAllByRole('radio').map((segment) => segment.getAttribute('value'))).toStrictEqual([
            'system',
            'light',
            'dark',
        ]);
    });

    it('marks the one in force, which is what the device opened in', () => {
        renderControl(<ThemeSegments onChoose={() => undefined} />);

        expect(screen.getByRole('radio', { name: 'Auto' })).toHaveProperty('checked', true);
        expect(screen.getByRole('radio', { name: 'Dark' })).toHaveProperty('checked', false);
    });

    it('marks the one the device already carries', () => {
        window.localStorage.setItem('mailfathom.theme', 'light');
        renderControl(<ThemeSegments onChoose={() => undefined} />);

        expect(screen.getByRole('radio', { name: 'Light' })).toHaveProperty('checked', true);
    });

    it('reports a chosen segment rather than deciding anything itself', () => {
        const onChoose = vi.fn();
        renderControl(<ThemeSegments onChoose={onChoose} />);

        fireEvent.click(screen.getByRole('radio', { name: 'Dark' }));

        expect(onChoose).toHaveBeenCalledWith('dark');
    });
});

describe('LanguageSegments', () => {
    afterEach(() => {
        window.localStorage.clear();
    });

    it('offers each language as one named group, each named in its own language', () => {
        renderControl(<LanguageSegments />);

        expect(screen.getByRole('group', { name: 'Language' })).toBeDefined();
        expect(screen.getAllByRole('radio').map((segment) => segment.getAttribute('value'))).toStrictEqual([
            'en',
            'pl',
        ]);
        expect(screen.getByRole('radio', { name: 'Polski' })).toBeDefined();
    });

    it('marks the language the client is reading in', () => {
        renderControl(<LanguageSegments />);

        expect(screen.getByRole('radio', { name: 'English' })).toHaveProperty('checked', true);
    });

    // Unlike the theme, which is answered by the deployment and reported upwards, the language is the device's own —
    // so what this control does about a choice is make it rather than state it.
    it('changes what the client reads in, and keeps it on the device', () => {
        renderControl(<LanguageSegments />);

        fireEvent.click(screen.getByRole('radio', { name: 'Polski' }));

        expect(screen.getByRole('radio', { name: 'Polski' })).toHaveProperty('checked', true);
        expect(window.localStorage.getItem('mailfathom.locale')).toBe('pl');
    });
});
