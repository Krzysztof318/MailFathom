// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { PlannedControl } from './PlannedControl';

describe('PlannedControl', () => {
    it('says in its own name that there is nothing behind it yet, so a reader is not offered an action', () => {
        render(
            <LocalizationProvider>
                <PlannedControl label="Archive" icon="archive" />
            </LocalizationProvider>,
        );

        const control = screen.getByRole('button', { name: 'Archive — not built yet' });

        expect(control.getAttribute('aria-disabled')).toBe('true');
    });

    it('stays reachable rather than disabled, so a screen reader still learns the product has it', () => {
        render(
            <LocalizationProvider>
                <PlannedControl label="Archive" icon="archive" shape="symbol" />
            </LocalizationProvider>,
        );

        expect(screen.getByRole('button', { name: 'Archive — not built yet' }).hasAttribute('disabled')).toBe(false);
    });

    it('shows the words on a labelled shape and only the symbol on a symbol shape', () => {
        const { rerender } = render(
            <LocalizationProvider>
                <PlannedControl label="Archive" icon="archive" />
            </LocalizationProvider>,
        );

        expect(screen.getByRole('button').textContent).toBe('Archive');

        rerender(
            <LocalizationProvider>
                <PlannedControl label="Archive" icon="archive" shape="symbol" />
            </LocalizationProvider>,
        );

        expect(screen.getByRole('button').textContent).toBe('');
    });
});
