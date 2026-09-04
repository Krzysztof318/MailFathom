// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientNotification } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { NotificationCentre } from './NotificationCentre';
import type { NotificationCentre as Centre } from './useNotificationCentre';
import type { PanelSwipe } from './usePanelSwipe';

// The panel is driven by a centre it is handed rather than by one that reads a deployment, because what is being
// proven here is what a person sees and what pressing something asks for. What the asking then does on the wire is
// `useNotificationCentre.test.tsx`.

const now = new Date('2026-09-04T12:00:00Z');

const mail: ClientNotification = {
    id: 'n-mail',
    kind: 'Mail',
    title: 'Ada Lovelace wrote',
    body: 'About the engine',
    source: 'Inbox',
    target: { kind: 'Message', storedEmailId: 'm-9' },
    occurredAt: '2026-09-04T11:55:00Z',
    read: false,
};

const meeting: ClientNotification = {
    id: 'n-meeting',
    kind: 'Calendar',
    title: 'Standing meeting moved',
    body: 'It is an hour later',
    source: null,
    target: { kind: 'Nothing' },
    occurredAt: '2026-09-03T09:00:00Z',
    read: true,
};

const swipe: PanelSwipe = {
    offset: null,
    dragging: false,
    springing: false,
    attachPanel: () => undefined,
    attachList: () => undefined,
    onNavigationPointerDown: () => undefined,
    onNavigationClickCapture: () => undefined,
    onPanelPointerDown: () => undefined,
};

const acts = {
    hide: vi.fn(),
    markRead: vi.fn(),
    markAllRead: vi.fn(),
    follow: vi.fn(),
    show: vi.fn(),
};

function centre(held: Partial<Centre> = {}): Centre {
    return {
        unreadCount: 1,
        shown: true,
        notifications: [mail, meeting],
        reading: false,
        failure: null,
        ...acts,
        ...held,
    };
}

function panel(held: Partial<Centre> = {}): void {
    render(
        <LocalizationProvider>
            <NotificationCentre centre={centre(held)} swipe={swipe} />
        </LocalizationProvider>,
    );
}

/** The row naming this notification, which is the list item its title sits in. */
function rowFor(title: string): HTMLElement {
    const heading = screen.getByText(title);
    const row = heading.closest('li');

    if (row === null) {
        throw new Error(`No row draws ${title}.`);
    }

    return row;
}

beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(now);
});

afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
});

describe('NotificationCentre', () => {
    it('opens as a dialog named for what it holds, so it is one thing a reader can move into', () => {
        panel();

        expect(screen.getByRole('dialog', { name: 'Notifications' })).toBeDefined();
    });

    it('stays off the screen while the centre is not being shown', () => {
        panel({ shown: false });

        expect(screen.queryByRole('dialog', { name: 'Notifications' })).toBeNull();
    });

    it('says how many arrived beside its own title', () => {
        panel({ unreadCount: 4 });

        expect(screen.getByText('4 new')).toBeDefined();
    });

    it('draws every notification on the tab that shows all of them', () => {
        panel();

        expect(screen.getAllByRole('listitem')).toHaveLength(2);
    });

    it('draws only what stands unread on the other tab, and says how much that is on the tab itself', () => {
        panel();

        fireEvent.click(screen.getByRole('radio', { name: 'Unread · 1' }));

        expect(screen.getAllByRole('listitem')).toHaveLength(1);
        expect(screen.getByText('Ada Lovelace wrote')).toBeDefined();
    });

    it('words how long ago a notification happened, rather than when it did', () => {
        panel();

        expect(rowFor('Ada Lovelace wrote').textContent).toContain('5 min. ago');
    });

    it('carries the instant itself behind the wording, which is what the machine-readable form is for', () => {
        panel();

        const when = rowFor('Ada Lovelace wrote').querySelector('time');

        expect(when?.getAttribute('datetime')).toBe('2026-09-04T11:55:00Z');
    });

    it('says what a notification says and where it came from', () => {
        panel();

        const row = rowFor('Ada Lovelace wrote');

        expect(row.textContent).toContain('About the engine');
        expect(row.textContent).toContain('Inbox');
    });

    it('falls back to the kind where a notification names no source of its own', () => {
        panel();

        expect(rowFor('Standing meeting moved').textContent).toContain('Calendar');
    });

    it('says in words that a row stands unread, for a reader who is looking at neither weight nor colour', () => {
        panel();

        expect(rowFor('Ada Lovelace wrote').textContent).toContain('Unread');
        expect(rowFor('Standing meeting moved').textContent).not.toContain('Unread');
    });

    it('marks one notification read from its own control, without opening it', () => {
        panel();

        fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }));

        expect(acts.markRead).toHaveBeenCalledWith(['n-mail'], true);
        expect(acts.follow).not.toHaveBeenCalled();
        expect(acts.hide).not.toHaveBeenCalled();
    });

    it('marks a notification unread again from the same control, which says what it would do', () => {
        panel();

        fireEvent.click(screen.getByRole('button', { name: 'Mark as unread' }));

        expect(acts.markRead).toHaveBeenCalledWith(['n-meeting'], false);
    });

    it('marks the whole centre read in one act', () => {
        panel();

        fireEvent.click(screen.getByRole('button', { name: 'Mark all as read' }));

        expect(acts.markAllRead).toHaveBeenCalledOnce();
    });

    it('offers nothing to mark where nothing stands unread', () => {
        panel({ unreadCount: 0 });

        expect(screen.queryByRole('button', { name: 'Mark all as read' })).toBeNull();
    });

    it('goes where a notification leads when its row is pressed, and takes the panel off the screen with it', () => {
        panel();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), { pointerType: 'mouse', button: 0 });

        expect(acts.follow).toHaveBeenCalledWith(mail);
    });

    it('leaves the panel by its own control', () => {
        panel();

        fireEvent.click(screen.getByRole('button', { name: 'Close notifications' }));

        expect(acts.hide).toHaveBeenCalledOnce();
    });

    it('leaves the panel through the centre rather than round it when the dialog is cancelled', () => {
        panel();

        fireEvent(screen.getByRole('dialog'), new Event('cancel', { bubbles: false, cancelable: true }));

        expect(acts.hide).toHaveBeenCalledOnce();
    });

    it('says that nothing is waiting, rather than drawing an empty list', () => {
        panel({ notifications: [], unreadCount: 0 });

        expect(screen.getByText('Nothing new. Everything has been read.')).toBeDefined();
    });

    it('says it is reading while the first page is still in flight', () => {
        panel({ notifications: [], reading: true, unreadCount: 0 });

        expect(screen.getByRole('status').textContent).toBe('Reading what has happened…');
    });

    it('says what failed and what it means, rather than that something went wrong', () => {
        panel({ notifications: [], failure: 'unavailable', unreadCount: 0 });

        expect(screen.getByRole('alert').textContent).toContain('Your notifications could not be read');
    });

    it('picks a row out under a held modifier rather than opening it', () => {
        panel();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });

        expect(acts.follow).not.toHaveBeenCalled();
        expect(screen.getByRole('toolbar', { name: 'Actions on the notifications selected' })).toBeDefined();
    });

    it('acts on everything picked out, in one request rather than one per row', () => {
        panel();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });
        fireEvent.pointerDown(screen.getByText('Standing meeting moved'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });
        const bar = within(screen.getByRole('toolbar', { name: 'Actions on the notifications selected' }));

        expect(bar.getByRole('status').textContent).toBe('2 selected');

        fireEvent.click(bar.getByRole('button', { name: 'Mark as read' }));

        expect(acts.markRead).toHaveBeenCalledWith(['n-mail', 'n-meeting'], true);
    });

    it('opens a row’s own menu on a right-click, where a selection starts and the source is opened from', () => {
        panel();

        fireEvent.contextMenu(screen.getByText('Ada Lovelace wrote'));

        expect(screen.getByRole('menuitem', { name: 'Select notifications' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Mark as read' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Open the source' })).toBeDefined();
    });

    it('leaves the source out of the menu on a notification that leads nowhere', () => {
        panel();

        fireEvent.contextMenu(screen.getByText('Standing meeting moved'));

        expect(screen.queryByRole('menuitem', { name: 'Open the source' })).toBeNull();
        expect(screen.getByRole('menuitem', { name: 'Mark as unread' })).toBeDefined();
    });
});
