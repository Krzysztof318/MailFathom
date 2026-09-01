// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailTimelineEntry, MailTimelinePage } from '@mailfathom/client-backend';
import {
    answered,
    cursorAfter,
    cursorBefore,
    heldRows,
    nothingHeld,
    pagesKeptEitherSide,
    positionOfRow,
    rowAt,
    rowCountOf,
    rowOfSlot,
    trimmedAround,
    wantedFor,
    type HeldTimeline,
    type TimelineRead,
} from './heldTimeline';

const rowsPerPage = 4;

function message(at: number): MailTimelineEntry {
    return {
        id: `message-${String(at)}`,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: `Message ${String(at)}`,
        receivedAt: '2026-08-31T09:41:00+00:00',
        sentAt: null,
        senderAddress: 'auditor@example.invalid',
        senderDisplayName: null,
        toAddresses: [],
        unread: false,
        flagged: false,
        answered: false,
        hasAttachments: false,
        attachmentCount: 0,
        sizeOctets: 1_024,
        preview: null,
    };
}

// One page of four, numbered from where it starts, so a row identity says which page it came from.
function page(from: number, cursors: Partial<MailTimelinePage> = {}): MailTimelinePage {
    return {
        emails: Array.from({ length: rowsPerPage }, (_, at) => message(from + at)),
        nextCursor: `after-${String(from + rowsPerPage)}`,
        previousCursor: from === 0 ? null : `before-${String(from)}`,
        pageSize: rowsPerPage,
        ...cursors,
    };
}

const forward: TimelineRead = { cursor: null, direction: 'forward', refilling: null };

function extending(cursor: string): TimelineRead {
    return { cursor, direction: 'forward', refilling: null };
}

/** A list read forward from its leading end, one page at a time, which is what scrolling down produces. */
function readForward(pages: number): HeldTimeline {
    let held = answered(nothingHeld, page(0), forward);

    for (let at = 1; at < pages; at += 1) {
        held = answered(held, page(at * rowsPerPage), extending(`after-${String(at * rowsPerPage)}`));
    }

    return held;
}

describe('answered', () => {
    it('stands a list on the first page it read', () => {
        const held = answered(nothingHeld, page(0), forward);

        expect(rowCountOf(held)).toBe(rowsPerPage);
        expect(rowAt(held, 0)?.id).toBe('message-0');
    });

    it('records a folder holding no mail rather than reading it again', () => {
        const empty = { emails: [], nextCursor: null, previousCursor: null, pageSize: rowsPerPage };
        const held = answered(nothingHeld, empty, forward);

        expect(held.slots).toHaveLength(1);
        expect(rowCountOf(held)).toBe(0);
        expect(wantedFor(held, 0, 0)).toBeNull();
    });

    it('joins a page read forward to the end of the list', () => {
        const held = readForward(2);

        expect(rowCountOf(held)).toBe(8);
        expect(rowAt(held, 7)?.id).toBe('message-7');
    });

    it('joins a page read backward to the beginning of the list', () => {
        const opened = answered(nothingHeld, page(4), forward);
        const held = answered(opened, page(0), { cursor: 'before-4', direction: 'backward', refilling: null });

        expect(rowAt(held, 0)?.id).toBe('message-0');
        expect(rowAt(held, 4)?.id).toBe('message-4');
    });

    it('retires the cursor at an end that answered with nothing, so the list stops asking there', () => {
        const opened = answered(nothingHeld, page(0), forward);
        const empty = { emails: [], nextCursor: null, previousCursor: null, pageSize: rowsPerPage };
        const held = answered(opened, empty, extending('after-4'));

        expect(rowCountOf(held)).toBe(rowsPerPage);
        expect(cursorAfter(held)).toBeNull();
    });

    it('puts a page read again back where its rows stood, rather than at an end', () => {
        const held = readForward(3);
        const dropped = trimmedAround(held, 8, 11);
        const refilled = answered(dropped, page(0), { cursor: null, direction: 'forward', refilling: 0 });

        expect(rowCountOf(refilled)).toBe(rowCountOf(held));
        expect(rowAt(refilled, 0)?.id).toBe('message-0');
    });
});

describe('trimmedAround', () => {
    it('drops the rows of a page far from the reader and keeps the space they stood in', () => {
        const held = readForward(2 + pagesKeptEitherSide * 2);
        const trimmed = trimmedAround(held, 0, 3);

        expect(rowCountOf(trimmed)).toBe(rowCountOf(held));
        expect(rowAt(trimmed, rowCountOf(held) - 1)).toBeNull();
    });

    it('keeps the pages either side of the one being read', () => {
        const held = readForward(6);
        const trimmed = trimmedAround(held, 8, 11);

        expect(rowAt(trimmed, 0)?.id).toBe('message-0');
        expect(rowAt(trimmed, 19)?.id).toBe('message-19');
    });

    it('answers with the list it was given where nothing was far enough to drop', () => {
        const held = readForward(2);

        expect(trimmedAround(held, 0, 7)).toBe(held);
    });

    it('leaves a page it already dropped alone rather than reporting a change', () => {
        const held = trimmedAround(readForward(6), 0, 3);

        expect(trimmedAround(held, 0, 3)).toBe(held);
    });
});

describe('wantedFor', () => {
    it('wants nothing while the window is inside rows the list is holding', () => {
        expect(wantedFor(readForward(3), 4, 7)).toBeNull();
    });

    it('wants the page after the end once the window reaches it', () => {
        expect(wantedFor(readForward(2), 4, 7)).toStrictEqual({
            cursor: 'after-8',
            direction: 'forward',
            refilling: null,
        });
    });

    it('wants the page before the beginning once the window reaches it', () => {
        const held = answered(nothingHeld, page(4, { nextCursor: null }), forward);

        expect(wantedFor(held, 0, 3)).toStrictEqual({
            cursor: 'before-4',
            direction: 'backward',
            refilling: null,
        });
    });

    it('reads onward before it reads back, where a short list has both of its ends on the screen', () => {
        const held = answered(nothingHeld, page(4), forward);

        expect(wantedFor(held, 0, 3)?.direction).toBe('forward');
    });

    it('wants a dropped page back through the cursor it was read under, not the leading end of the list', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(wantedFor(held, 0, 3)).toStrictEqual({ cursor: null, direction: 'forward', refilling: 0 });
    });

    it('wants a dropped page before either end, so a hole on the screen is filled before the list grows', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(wantedFor(held, 0, 23)?.refilling).toBe(0);
    });

    it('wants nothing at an end the list has reached', () => {
        const held = answered(nothingHeld, page(0, { nextCursor: null, previousCursor: null }), forward);

        expect(wantedFor(held, 0, 3)).toBeNull();
    });
});

describe('positionOfRow', () => {
    it('answers the page a row is in and where in it, so a returning visit reads that page', () => {
        expect(positionOfRow(readForward(3), 9)).toStrictEqual({
            cursor: 'after-8',
            readAs: 'forward',
            rowInPage: 1,
        });
    });

    it('answers the leading page for a row in it', () => {
        expect(positionOfRow(readForward(2), 1)).toStrictEqual({ cursor: null, readAs: 'forward', rowInPage: 1 });
    });

    it('answers nothing for a row past the end of the list', () => {
        expect(positionOfRow(readForward(1), 99)).toBeNull();
    });
});

describe('rowOfSlot', () => {
    it('answers where a page starts', () => {
        expect(rowOfSlot(readForward(3), 2)).toBe(8);
    });

    it('answers nothing for a page the list does not know', () => {
        expect(rowOfSlot(readForward(1), 4)).toBeNull();
    });
});

describe('heldRows', () => {
    it('answers only the rows the list is holding, in reading order', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(heldRows(held).map((email) => email.id)).not.toContain('message-0');
        expect(heldRows(held)[0]?.id).toBe(`message-${String(rowsPerPage * (6 - 1 - pagesKeptEitherSide))}`);
    });
});

describe('cursorBefore', () => {
    it('answers nothing at the beginning of a list read from its leading end', () => {
        expect(cursorBefore(readForward(2))).toBeNull();
    });
});
