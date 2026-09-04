// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { mostUnreadShown, NotificationBell } from './NotificationBell';

// The badge is a picture of a number and the name is the number itself, so both are asserted: what a reader sees and
// what a reader who cannot see it is told are different renderings of one count, and only one of them is capped.

function bell(unreadCount: number, shown = false, onPress: () => void = () => undefined): void {
    render(
        <LocalizationProvider>
            <NotificationBell unreadCount={unreadCount} shown={shown} onPress={onPress} />
        </LocalizationProvider>,
    );
}

describe('NotificationBell', () => {
    it('is named for what it opens where nothing is waiting, rather than for a count of nothing', () => {
        bell(0);

        expect(screen.getByRole('button', { name: 'Notifications' })).toBeDefined();
    });

    it('says how many stand unread in its own name, in the form the count takes', () => {
        bell(1);

        expect(screen.getByRole('button', { name: '1 unread notification' })).toBeDefined();
    });

    it('says a count above the badge’s cap in full, because only the badge is a picture', () => {
        bell(42);

        expect(screen.getByRole('button', { name: '42 unread notifications' })).toBeDefined();
    });

    it('draws the count on the badge while it is small enough to read', () => {
        bell(3);

        expect(screen.getByRole('button').textContent).toContain('3');
    });

    it('draws the cap and a plus past it, the difference between ten and eleven not being what a badge is for', () => {
        bell(mostUnreadShown + 1);

        expect(screen.getByRole('button').textContent).toContain(`${String(mostUnreadShown)}+`);
    });

    it('draws no badge at all where nothing is waiting', () => {
        bell(0);

        expect(screen.getByRole('button').textContent).toBe('Notifications');
    });

    it('says whether the panel it opens is open, so the control describes itself rather than the screen', () => {
        bell(0, true);

        expect(screen.getByRole('button').getAttribute('aria-expanded')).toBe('true');
    });

    it('asks for the panel when it is pressed', () => {
        const pressed = vi.fn();

        bell(2, false, pressed);
        fireEvent.click(screen.getByRole('button'));

        expect(pressed).toHaveBeenCalledOnce();
    });
});
