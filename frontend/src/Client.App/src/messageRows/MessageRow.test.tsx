// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { pressDrift, pressOpensAfter } from '../contextMenu/rowPress';
import { MailboxActsContext, nothingActed, type MailboxAct, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { ReadMarkingContext, nothingMarkedRead, type MarkedIn, type ReadMarking } from '../readMarking/useReadMarking';
import { MessageRow } from './MessageRow';

const email: MailTimelineEntry = {
    id: 'message-1',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    subject: 'The quarter is closed',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: null,
    senderAddress: 'writer@nordwind.example',
    senderDisplayName: 'Writer',
    toAddresses: ['owner@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The opening of the message.',
};

function drawRow(
    note?: string,
    unread = false,
    marking: ReadMarking = nothingMarkedRead,
    acts: MailboxActs = nothingActed,
    flagged = false,
): HTMLElement {
    render(
        <LocalizationProvider>
            <ReadMarkingContext value={marking}>
                <MailboxActsContext value={acts}>
                    <ul>
                        <MessageRow
                            email={{ ...email, unread, flagged }}
                            position={1}
                            open={false}
                            selected={false}
                            focusable
                            note={note}
                            onOpen={() => undefined}
                            onPoint={() => undefined}
                            onPointerEnter={() => undefined}
                            onElement={() => undefined}
                        />
                    </ul>
                </MailboxActsContext>
            </ReadMarkingContext>
        </LocalizationProvider>,
    );

    return screen.getByRole('option');
}

// The row as a pointer meets it: what pointing at it came to, and what a press on it opened. The clock is fake because
// the press is a timer, and it is released after every test in this file.
function pointedRow({
    onPoint = vi.fn(),
    onPress = vi.fn(),
}: {
    onPoint?: (event: unknown) => void;
    onPress?: (at: MenuPoint) => void;
} = {}): HTMLElement {
    vi.useFakeTimers();

    render(
        <LocalizationProvider>
            <ul>
                <MessageRow
                    email={email}
                    position={1}
                    open={false}
                    selected={false}
                    focusable
                    onOpen={() => undefined}
                    onPoint={onPoint}
                    onPress={onPress}
                    onPointerEnter={() => undefined}
                    onElement={() => undefined}
                />
            </ul>
        </LocalizationProvider>,
    );

    return screen.getByRole('option');
}

afterEach(() => {
    vi.useRealTimers();
});

/** What a client that has asked for this act on exactly this message carries, pending the deployment's own pass. */
function asking(act: MailboxAct, storedEmailId = email.id): MailboxActs {
    return { ...nothingActed, asked: new Map([[storedEmailId, act]]) };
}

/** What a client that has marked exactly this message read carries, which is what a row reads its state through. */
function marked(storedEmailId: string, place: MarkedIn = { account: 'work', folder: 'INBOX' }): ReadMarking {
    return { marked: new Map([[storedEmailId, place]]), markRead: () => undefined };
}

// The line the row's height reserves whether or not anything is in it, which is what lets the search's row and the
// folder's row be one row. Whether it is announced is the whole of what the two cases differ by, and nothing a reader
// sees says which happened — so the attribute is what is asserted here.
describe('MessageRow', () => {
    it('announces the reserved line when the row has something to say about the message', () => {
        const reserved = drawRow('Found by what it means.').lastElementChild;

        expect(reserved?.textContent).toBe('Found by what it means.');
        expect(reserved?.getAttribute('aria-hidden')).toBeNull();
    });

    it('keeps the reserved line out of the accessibility tree when the row has nothing to say', () => {
        const reserved = drawRow().lastElementChild;

        expect(reserved?.textContent).toBe('');
        expect(reserved?.getAttribute('aria-hidden')).toBe('true');
    });

    // Unread is drawn as weight and colour, which a test cannot read, and said in as many words for somebody who is
    // looking at neither — so the words are what says whether the row drew the message read.
    it('says a message the deployment reports as unread is unread', () => {
        drawRow(undefined, true);

        expect(screen.getByText('Unread')).toBeDefined();
    });

    it('says nothing of the sort for a message the deployment reports as read', () => {
        drawRow();

        expect(screen.queryByText('Unread')).toBeNull();
    });

    // The row draws from the pending mutation rather than waiting for the account's own pass to observe the flag,
    // which is the whole of what a folder's count and its rows have to agree about.
    it('draws a message this client has marked read as read, though the deployment still reports it unread', () => {
        drawRow(undefined, true, marked(email.id));

        expect(screen.queryByText('Unread')).toBeNull();
    });

    it('leaves a message another one’s marking has nothing to do with unread', () => {
        drawRow(undefined, true, marked('another-message'));

        expect(screen.getByText('Unread')).toBeDefined();
    });

    // An act writes a record the account's own pass issues later, so the row says what was asked for from the press
    // rather than waiting for a mail server nobody may be able to reach.
    it.each([
        ['archive', 'Archiving…'],
        ['delete', 'Moving to the trash…'],
        ['flag', 'Flagging…'],
        ['markUnread', 'Marking unread…'],
        ['move', 'Filing…'],
    ] as const)('says a message asked to be %sd is being acted on, in the reserved line', (act, said) => {
        const reserved = drawRow(undefined, false, nothingMarkedRead, asking(act)).lastElementChild;

        expect(reserved?.textContent).toBe(said);
        expect(reserved?.getAttribute('aria-hidden')).toBeNull();
    });

    it('draws a message asked to be marked unread as unread, rather than waiting for the server to report it', () => {
        drawRow(undefined, false, nothingMarkedRead, asking('markUnread'));

        expect(screen.getByText('Unread')).toBeDefined();
    });

    it('stops saying so once the deployment reports the flag the act asked for, which is what retires it', () => {
        const reserved = drawRow(undefined, false, nothingMarkedRead, asking('flag'), true).lastElementChild;

        expect(reserved?.textContent).toBe('');
    });

    it('says nothing of another message’s act, the pending line belonging to the row it is about', () => {
        const reserved = drawRow(
            undefined,
            false,
            nothingMarkedRead,
            asking('archive', 'another-message'),
        ).lastElementChild;

        expect(reserved?.textContent).toBe('');
    });
});

describe('MessageRow, under a pointer', () => {
    it('acts as a mouse goes down, because that same press may go on to sweep a run of rows', () => {
        const pointed = vi.fn();
        const row = pointedRow({ onPoint: pointed });

        fireEvent.pointerDown(row, { pointerType: 'mouse' });

        expect(pointed).toHaveBeenCalledOnce();
    });

    it('waits for a finger to be lifted before it acts, the same touch being able to become a press', () => {
        const pointed = vi.fn();
        const row = pointedRow({ onPoint: pointed });

        fireEvent.pointerDown(row, { pointerType: 'touch', clientX: 10, clientY: 10 });

        expect(pointed).not.toHaveBeenCalled();

        fireEvent.pointerUp(row, { pointerType: 'touch', clientX: 10, clientY: 10 });

        expect(pointed).toHaveBeenCalledOnce();
    });

    it('opens the row’s menu once the finger has been held, and acts on nothing when it is lifted', () => {
        const pointed = vi.fn();
        const pressed = vi.fn();
        const row = pointedRow({ onPoint: pointed, onPress: pressed });

        fireEvent.pointerDown(row, { pointerType: 'touch', clientX: 10, clientY: 20 });

        act(() => {
            vi.advanceTimersByTime(pressOpensAfter);
        });

        fireEvent.pointerUp(row, { pointerType: 'touch', clientX: 10, clientY: 20 });

        expect(pressed).toHaveBeenCalledWith({ x: 10, y: 20 });
        expect(pointed).not.toHaveBeenCalled();
    });

    it('opens nothing where the finger travelled, which is a list being scrolled rather than a row being held', () => {
        const pressed = vi.fn();
        const row = pointedRow({ onPress: pressed });

        fireEvent.pointerDown(row, { pointerType: 'touch', clientX: 10, clientY: 20 });
        fireEvent.pointerMove(row, { pointerType: 'touch', clientX: 10, clientY: 20 + pressDrift + 1 });

        act(() => {
            vi.advanceTimersByTime(pressOpensAfter);
        });

        expect(pressed).not.toHaveBeenCalled();
    });

    it('opens the row’s menu on the pointer’s own menu gesture', () => {
        const pressed = vi.fn();
        const row = pointedRow({ onPress: pressed });

        fireEvent.contextMenu(row, { clientX: 33, clientY: 44 });

        expect(pressed).toHaveBeenCalledWith({ x: 33, y: 44 });
    });
});
