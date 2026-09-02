// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { deviceStore, listWidthKey } from '../device/deviceStore';

// How wide the message list is drawn beside the reading pane, which is a decision the person reading makes and this
// machine keeps. Everything here is arithmetic over one number, so it is a module of its own rather than state inside
// the component: the bounds, the reset, the clamp a narrower window forces, and where the chosen width is written are
// each testable without rendering three columns.
//
// The four widths below are the design project's and are written here rather than in `styles.css` because nothing in
// the markup spells them: what reads them is the drag arithmetic, the keyboard step, and the position the grip reports
// as a separator. A token would be a second copy of each number that only the stylesheet could see.

/** The narrowest the list is drawn at, past which a sender and a subject stop fitting on one row. */
export const narrowestList = 232;

/** The widest the list is drawn at, past which it stops being a list beside a message and becomes the screen. */
export const widestList = 560;

/** What a first run opens at, and what a reset returns to. */
export const startingListWidth = 340;

/**
 * The least room the list leaves the reading pane.
 *
 * It is the narrowest viewport the client is built for, which is the width below which a message stops being readable
 * at all — so a window with less than this beside the list has nothing left to give and the list keeps its own
 * minimum instead.
 */
export const leastReadingWidth = 320;

/** How far one keyboard step moves the split, which is a step a reader can see land rather than a pixel. */
export const listWidthStep = 16;

/**
 * The width actually drawn, given how much room the list and the reading pane have between them.
 *
 * @param width The width somebody chose, which may be one this window no longer has room for.
 * @param room How many pixels the two columns share. `Number.POSITIVE_INFINITY` where nothing has measured yet.
 * @returns A width inside the bounds that leaves the reading pane something to draw in.
 */
export function listWidthWithin(width: number, room: number): number {
    const most = Math.max(narrowestList, Math.min(widestList, room - leastReadingWidth));

    return Math.round(Math.min(Math.max(width, narrowestList), most));
}

/**
 * The width this person last chose on this machine, or the starting width where they chose none.
 *
 * Read back as untrusted input, because a device store is a place a person can write: anything that is not a number
 * inside the bounds is answered as nothing chosen rather than as a column drawn off the screen.
 */
export function readListWidth(person: string | null): number {
    if (person === null) {
        return startingListWidth;
    }

    const stored = Number(deviceStore().read(listWidthKey(person)));

    return Number.isFinite(stored) && stored > 0
        ? listWidthWithin(stored, Number.POSITIVE_INFINITY)
        : startingListWidth;
}

/** Keeps the width this person settled on, so the next start of the client opens the Mail space at it. */
export function storeListWidth(person: string | null, width: number): void {
    if (person === null) {
        return;
    }

    deviceStore().write(listWidthKey(person), String(Math.round(width)));
}
