// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ListedMailContext, nothingListed } from '../messageList/useListedMail';
import { Refresh } from './Refresh';

function drawing(readListAgain: () => void, readNotificationsAgain: () => void): void {
    render(
        <LocalizationProvider>
            <ListedMailContext value={{ ...nothingListed, readAgain: readListAgain }}>
                <Refresh readNotificationsAgain={readNotificationsAgain} />
            </ListedMailContext>
        </LocalizationProvider>,
    );
}

describe('Refresh', () => {
    it('reads the list and the notification centre again on one press, which is what a global refresh means', () => {
        const list = vi.fn();
        const centre = vi.fn();

        drawing(list, centre);
        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

        expect(list).toHaveBeenCalledTimes(1);
        expect(centre).toHaveBeenCalledTimes(1);
    });

    it('asks the list nothing on a screen with no list on it, rather than refusing the press', () => {
        const centre = vi.fn();

        render(
            <LocalizationProvider>
                <Refresh readNotificationsAgain={centre} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

        expect(centre).toHaveBeenCalledTimes(1);
    });

    it('carries a name that says what it does rather than what it looks like', () => {
        drawing(
            () => undefined,
            () => undefined,
        );

        expect(screen.getByRole('button', { name: 'Refresh' })).toBeDefined();
    });
});
