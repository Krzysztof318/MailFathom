// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { pressDrift, pressOpensAfter } from '../contextMenu/rowPress';
import { swipeCarriesTo, swipeDistance, swipeDrift, swipeEngages } from '../controls/swipeAcross';
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
    toAddresses: ['user@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The opening of the message.',
    enrichment: null,
    threadMessageCount: null,
};

function drawMoved(moved: { readonly arrived?: boolean; readonly changed?: boolean }): HTMLElement {
    render(
        <LocalizationProvider>
            <ul>
                <MessageRow
                    email={email}
                    position={1}
                    open={false}
                    selected={false}
                    focusable
                    arrived={moved.arrived ?? false}
                    changed={moved.changed ?? false}
                    onOpen={() => undefined}
                    onPoint={() => undefined}
                    onPointerEnter={() => undefined}
                    onElement={() => undefined}
                />
            </ul>
        </LocalizationProvider>,
    );

    return screen.getByRole('option');
}

function drawRow(
    note?: string,
    unread = false,
    marking: ReadMarking = nothingMarkedRead,
    acts: MailboxActs = nothingActed,
    flagged = false,
    threadMessageCount: number | null = null,
): HTMLElement {
    render(
        <LocalizationProvider>
            <ReadMarkingContext value={marking}>
                <MailboxActsContext value={acts}>
                    <ul>
                        <MessageRow
                            email={{ ...email, unread, flagged, threadMessageCount }}
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
    onAnswer,
    onArchive,
}: {
    onPoint?: (event: unknown) => void;
    onPress?: (at: MenuPoint) => void;
    onAnswer?: (() => void) | undefined;
    onArchive?: (() => void) | undefined;
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
                    onAnswer={onAnswer}
                    onArchive={onArchive}
                    onPointerEnter={() => undefined}
                    onElement={() => undefined}
                />
            </ul>
        </LocalizationProvider>,
    );

    return screen.getByRole('option');
}

/** A finger landing on the row, travelling to where it is left, and lifting there. */
function swipe(row: HTMLElement, across: number, down = 0): void {
    const landed = { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 };
    const travelled = { pointerId: 1, pointerType: 'touch', clientX: across, clientY: down };

    fireEvent.pointerDown(row, landed);
    fireEvent.pointerMove(row, travelled);
    fireEvent.pointerUp(row, travelled);
}

/** How far the row itself has been carried from where the list drew it, in CSS pixels. */
function carriedTo(row: HTMLElement): string {
    return (row.lastElementChild as HTMLElement | null)?.style.transform ?? '';
}

afterEach(() => {
    vi.useRealTimers();
});

/**
 * The line the row's height reserves, which is the last of what the carried row draws.
 *
 * Two elements down rather than one, because the row a finger carries aside is inside the element the list positions:
 * what a swipe uncovers stands behind that, and the height and the line between rows belong to the outer one.
 */
function reservedLine(row: HTMLElement): Element | null {
    return row.lastElementChild?.lastElementChild ?? null;
}

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
        const reserved = reservedLine(drawRow('Found by what it means.'));

        expect(reserved?.textContent).toBe('Found by what it means.');
        expect(reserved?.getAttribute('aria-hidden')).toBeNull();
    });

    it('keeps the reserved line out of the accessibility tree when the row has nothing to say', () => {
        const reserved = reservedLine(drawRow());

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

    // How long the correspondence is, which the design draws as a number alone — so the words beside it are what a
    // reader who is not looking at the row gets, and both are asserted together. A conversation of one is every
    // message that was never answered, so drawing the pill over it would put a mark on nearly every row in a folder.
    it('says how many messages the correspondence holds where it holds more than the one', () => {
        drawRow(undefined, false, nothingMarkedRead, nothingActed, false, 6);

        expect(screen.getByText('6')).toBeDefined();
        expect(screen.getByText('6 messages in this conversation')).toBeDefined();
    });

    it('says nothing of the sort where the correspondence is the one message', () => {
        drawRow(undefined, false, nothingMarkedRead, nothingActed, false, 1);

        expect(screen.queryByText('1 messages in this conversation')).toBeNull();
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
        const reserved = reservedLine(drawRow(undefined, false, nothingMarkedRead, asking(act)));

        expect(reserved?.textContent).toBe(said);
        expect(reserved?.getAttribute('aria-hidden')).toBeNull();
    });

    // Taking a flag off is asked for from the head of the open message rather than from a row, and the row it is about
    // still says so: what a surface reports is the act, not the surface it was pressed on.
    it('says a flagged message asked to lose its flag is being acted on, in the same reserved line', () => {
        const reserved = reservedLine(drawRow(undefined, false, nothingMarkedRead, asking('unflag'), true));

        expect(reserved?.textContent).toBe('Removing the flag…');
    });

    it('draws a message asked to be marked unread as unread, rather than waiting for the server to report it', () => {
        drawRow(undefined, false, nothingMarkedRead, asking('markUnread'));

        expect(screen.getByText('Unread')).toBeDefined();
    });

    it('stops saying so once the deployment reports the flag the act asked for, which is what retires it', () => {
        const reserved = reservedLine(drawRow(undefined, false, nothingMarkedRead, asking('flag'), true));

        expect(reserved?.textContent).toBe('');
    });

    it('says nothing of another message’s act, the pending line belonging to the row it is about', () => {
        const reserved = reservedLine(
            drawRow(undefined, false, nothingMarkedRead, asking('archive', 'another-message')),
        );

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

// The other thing one finger on a row can mean. The two directions are the design project's own — left to answer the
// message, right to file it away — and everything below is a rule about when the row acts on neither.
describe('MessageRow, under a finger carried across it', () => {
    it('answers the message when the finger goes left past the threshold, and opens it as it does', () => {
        const answered = vi.fn();
        const archived = vi.fn();
        const row = pointedRow({ onAnswer: answered, onArchive: archived });

        swipe(row, -swipeDistance);

        expect(answered).toHaveBeenCalledOnce();
        expect(archived).not.toHaveBeenCalled();
    });

    it('files the message away when it goes right past the threshold', () => {
        const answered = vi.fn();
        const archived = vi.fn();
        const row = pointedRow({ onAnswer: answered, onArchive: archived });

        swipe(row, swipeDistance);

        expect(archived).toHaveBeenCalledOnce();
        expect(answered).not.toHaveBeenCalled();
    });

    it('springs back and does nothing where the finger stopped short of it', () => {
        const archived = vi.fn();
        const row = pointedRow({ onArchive: archived });

        swipe(row, swipeDistance - 1);

        expect(archived).not.toHaveBeenCalled();
        expect(carriedTo(row)).toBe('');
    });

    // A person scrolling a list is scrolling, and a row that filed mail at the end of it would be the fastest way in
    // the client to lose a message by accident.
    it('is off once the finger has gone further up or down than it has gone sideways', () => {
        const archived = vi.fn();
        const row = pointedRow({ onArchive: archived });

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: 10, clientY: swipeDrift + 1 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: swipeDistance, clientY: 0 });
        fireEvent.pointerUp(row, { pointerId: 1, pointerType: 'touch', clientX: swipeDistance, clientY: 0 });

        expect(archived).not.toHaveBeenCalled();
    });

    it('carries the row under the finger once the gesture has engaged, and no further than the design draws it', () => {
        const row = pointedRow({ onArchive: vi.fn() });

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: swipeEngages - 1, clientY: 0 });

        expect(carriedTo(row)).toBe('');

        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: swipeCarriesTo + 200, clientY: 0 });

        expect(carriedTo(row)).toBe(`translateX(${String(swipeCarriesTo)}px)`);
    });

    it('draws what it is about to do, faintly until the finger has gone far enough', () => {
        const row = pointedRow({ onArchive: vi.fn() });

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: swipeDistance - 1, clientY: 0 });

        expect(screen.getByText('Archive').parentElement?.className).toContain('opacity-55');

        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: swipeDistance, clientY: 0 });

        expect(screen.getByText('Archive').parentElement?.className).toContain('opacity-100');
    });

    it('shows nothing and acts on nothing in a direction this list does not offer', () => {
        const row = pointedRow({ onArchive: vi.fn() });

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: -swipeDistance, clientY: 0 });

        expect(screen.queryByText('Reply')).toBeNull();
        expect(carriedTo(row)).toBe('');
    });

    // The lift that finished the swipe is also a tap, and a row that acted on both would open what it just filed.
    it('suppresses the tap behind a swipe that acted', () => {
        const pointed = vi.fn();
        const row = pointedRow({ onPoint: pointed, onArchive: vi.fn() });

        swipe(row, swipeDistance);

        expect(pointed).not.toHaveBeenCalled();
    });

    it('leaves the tap alone where the swipe sprang back, that lift being an ordinary tap', () => {
        const pointed = vi.fn();
        const row = pointedRow({ onPoint: pointed, onArchive: vi.fn() });

        swipe(row, swipeEngages);

        expect(pointed).toHaveBeenCalledOnce();
    });

    // A mouse drag would file mail on a slipped button, and both acts are on the row's own menu and in the toolbar for
    // a pointer that has one.
    it('does nothing at all under a mouse, however far it is dragged', () => {
        const archived = vi.fn();
        const row = pointedRow({ onArchive: archived });

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'mouse', button: 0, clientX: 0, clientY: 0 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'mouse', clientX: swipeDistance, clientY: 0 });
        fireEvent.pointerUp(row, { pointerId: 1, pointerType: 'mouse', clientX: swipeDistance, clientY: 0 });

        expect(archived).not.toHaveBeenCalled();
        expect(carriedTo(row)).toBe('');
    });

    // One finger, and never both gestures: a menu already open is what the finger still on the row belongs to.
    it('carries nothing and acts on nothing once the press has opened the row’s menu', () => {
        const archived = vi.fn();
        const row = pointedRow({ onPress: vi.fn(), onArchive: archived });

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 });

        act(() => {
            vi.advanceTimersByTime(pressOpensAfter);
        });

        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: swipeDistance, clientY: 0 });
        fireEvent.pointerUp(row, { pointerId: 1, pointerType: 'touch', clientX: swipeDistance, clientY: 0 });

        expect(archived).not.toHaveBeenCalled();
        expect(carriedTo(row)).toBe('');
    });

    it('has already stopped arming the menu by the time the row is being carried', () => {
        const pressed = vi.fn();
        const row = pointedRow({ onPress: pressed, onArchive: vi.fn() });

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: swipeEngages, clientY: 0 });

        act(() => {
            vi.advanceTimersByTime(pressOpensAfter);
        });

        expect(pressed).not.toHaveBeenCalled();
    });
});

// The standing rule for every list in this client: the skeleton is drawn once, when the list holds nothing, and every
// change after it reaches the rows it touched. A row is where that lands, and what it draws is the one thing about it
// that moved.
describe('MessageRow, a row that moved', () => {
    it('lands into its place where it arrived in a list the reader was already looking at', () => {
        expect(drawMoved({ arrived: true }).className).toContain('animate-row-landing');
    });

    it('is washed and marked where the deployment said this message changed', () => {
        expect(drawMoved({ changed: true }).className).toContain('animate-row-changed');
    });

    it('draws the arrival alone where a row both arrived and was named as changed, having no version to change from', () => {
        const row = drawMoved({ arrived: true, changed: true });

        expect(row.className).toContain('animate-row-landing');
        expect(row.className).not.toContain('animate-row-changed');
    });

    it('draws neither where nothing about the row moved, which is every row of a list that was just read', () => {
        const row = drawMoved({});

        expect(row.className).not.toContain('animate-row-landing');
        expect(row.className).not.toContain('animate-row-changed');
    });
});

// The mark the design draws on the tile, which opens what MailFathom read from the message. It is drawn out of the
// accessibility tree deliberately — a row is an `option` of a listbox and holds no focusable descendant — so it is
// found by the name it shows a pointer rather than by a role.
function markedRow({
    onReadings,
    onPoint = vi.fn(),
}: {
    onReadings?: (() => void) | undefined;
    onPoint?: (event: unknown) => void;
}): void {
    render(
        <LocalizationProvider>
            <ul>
                <MessageRow
                    email={email}
                    position={1}
                    open={false}
                    selected={false}
                    focusable
                    onReadings={onReadings}
                    onOpen={() => undefined}
                    onPoint={onPoint}
                    onPointerEnter={() => undefined}
                    onElement={() => undefined}
                />
            </ul>
        </LocalizationProvider>,
    );
}

describe('MessageRow, the mark that opens what was read', () => {
    it('opens the readings of the message it is drawn on', () => {
        const asked = vi.fn();

        markedRow({ onReadings: asked });
        fireEvent.click(screen.getByTitle('What MailFathom made of it'));

        expect(asked).toHaveBeenCalledOnce();
    });

    it('is drawn on no row whose message carries nothing that was read from it', () => {
        markedRow({});

        expect(screen.queryByTitle('What MailFathom made of it')).toBeNull();
    });

    it('leaves the row unselected, a press on the mark being about the mark rather than about the message', () => {
        const pointed = vi.fn();

        markedRow({ onReadings: () => undefined, onPoint: pointed });

        const mark = screen.getByTitle('What MailFathom made of it');

        fireEvent.pointerDown(mark, { pointerType: 'mouse' });
        fireEvent.pointerUp(mark, { pointerType: 'mouse' });

        expect(pointed).not.toHaveBeenCalled();
    });

    it('announces nothing of its own, the row menu being the path that carries this surface a name', () => {
        markedRow({ onReadings: () => undefined });

        expect(screen.getByTitle('What MailFathom made of it').getAttribute('aria-hidden')).toBe('true');
    });
});
