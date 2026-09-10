// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { deviceStore, mailboxesWidthKey } from '../device/deviceStore';

// How wide the mailbox column is drawn, which is `listWidth.ts` one column to the left and is a module of its own for
// the same reason: the bounds, the reset, and where the chosen width is written are arithmetic over one number, and
// each of them is testable without rendering three columns.
//
// The three widths are the design project's own — a column that opens at 210 and is dragged as far as 420 — and they
// are written here rather than in `styles.css` for the reason the list's are: what reads them is the drag arithmetic,
// the keyboard step, and the position the grip reports as a separator, none of which the markup spells.
//
// It carries no counterpart to the list's `leastReadingWidth`, and that absence is the one real difference between
// the two. The list and the reading pane share the room left over after this column, so the list has to leave its
// neighbour something to draw in; this column stands beside both of them and the pair below it does its own
// clamping against whatever room is left — which is why widening the mailboxes narrows the list rather than
// squeezing the message.

/** The narrowest the mailbox column is drawn at, which is the width the design project opens it at. */
export const narrowestMailboxes = 210;

/** The widest it is drawn at, past which it stops being a column of names and becomes a second list. */
export const widestMailboxes = 420;

/** What a first run opens at, and what a double-click on the grip returns to. */
export const startingMailboxesWidth = narrowestMailboxes;

/** How far one keyboard step moves the boundary, which is a step a reader can see land rather than a pixel. */
export const mailboxesWidthStep = 16;

/**
 * The width actually drawn, given the one somebody chose.
 *
 * @param width The width somebody chose, which a stored value or a drag past the edge can put outside the bounds.
 * @returns A width inside the bounds, rounded to whole pixels.
 */
export function mailboxesWidthWithin(width: number): number {
    return Math.round(Math.min(Math.max(width, narrowestMailboxes), widestMailboxes));
}

/**
 * The width this person last chose on this machine, or the starting width where they chose none.
 *
 * Read back as untrusted input, because a device store is a place a person can write: anything that is not a number
 * inside the bounds is answered as nothing chosen rather than as a column drawn off the screen.
 */
export function readMailboxesWidth(person: string | null): number {
    if (person === null) {
        return startingMailboxesWidth;
    }

    const stored = Number(deviceStore().read(mailboxesWidthKey(person)));

    return Number.isFinite(stored) && stored > 0 ? mailboxesWidthWithin(stored) : startingMailboxesWidth;
}

/** Keeps the width this person settled on, so the next start of the client opens the Mail space at it. */
export function storeMailboxesWidth(person: string | null, width: number): void {
    if (person === null) {
        return;
    }

    deviceStore().write(mailboxesWidthKey(person), String(Math.round(width)));
}
