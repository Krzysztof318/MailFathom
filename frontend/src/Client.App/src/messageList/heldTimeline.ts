// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailTimelineEntry, MailTimelinePage, MailTimelinePageDirection } from '@mailfathom/client-backend';

// What the list is holding, which is bounded as well as what it is drawing. Windowing alone bounds the document; this
// bounds the memory behind it, because a reader who has scrolled past forty thousand messages has read four hundred
// pages, and holding them would be the same defect one level down.
//
// A page whose rows are dropped keeps its place rather than leaving the list. It becomes a slot that still knows how
// many rows stood there and the cursor they were read under, so the list keeps its height, nothing the reader is
// looking at moves, and scrolling back into it reads that page again from its own cursor rather than reading the folder
// from its leading end. The alternative — dropping the slot too — would shrink the list under the reader on a scroll
// they did not make, which is the defect windowing exists to avoid one level up.

/** One page's worth of the list: its rows where they are held, and where they are read from where they are not. */
export interface TimelineSlot {
    /** The cursor this page was asked with, or `null` where it was read from the leading end of the list. */
    readonly askedWith: string | null;

    /** Which way it was read from that cursor, which is the other half of how it is read again. */
    readonly readAs: MailTimelinePageDirection;

    /** How many rows stand here, which the list keeps whether or not it is holding them. */
    readonly rowCount: number;

    /** The rows, or `null` where they have been dropped and the cursor above is how they come back. */
    readonly emails: readonly MailTimelineEntry[] | null;

    readonly nextCursor: string | null;
    readonly previousCursor: string | null;
}

/**
 * How many pages either side of what is being read keep their rows.
 *
 * Two pages of a hundred either side is a few screenfuls in both directions, which is further than ordinary scrolling
 * reaches between frames — so a reader moving at any speed a person moves at finds rows rather than the space they
 * stood in. Everything beyond it is dropped, so the list costs the same after an hour of scrolling as on the first
 * screen.
 */
export const pagesKeptEitherSide = 2;

/** The pages the list knows about, in the order the list is read in. */
export interface HeldTimeline {
    readonly slots: readonly TimelineSlot[];
}

/** A list that knows about nothing, which is where every scope and every change of filter starts. */
export const nothingHeld: HeldTimeline = { slots: [] };

/** How many rows the list stands for, held or dropped. */
export function rowCountOf(held: HeldTimeline): number {
    return held.slots.reduce((rows, slot) => rows + slot.rowCount, 0);
}

/** The message at a row, or `null` where the page holding it has been dropped. */
export function rowAt(held: HeldTimeline, row: number): MailTimelineEntry | null {
    let passed = 0;

    for (const slot of held.slots) {
        if (row < passed + slot.rowCount) {
            return slot.emails?.[row - passed] ?? null;
        }

        passed += slot.rowCount;
    }

    return null;
}

/** Every message the list is holding, in reading order, which is what the selection is ordered against. */
export function heldRows(held: HeldTimeline): readonly MailTimelineEntry[] {
    return held.slots.flatMap((slot) => slot.emails ?? []);
}

/** The cursor the page after the end of the list is asked with, or `null` where the end has been reached. */
export function cursorAfter(held: HeldTimeline): string | null {
    return held.slots.at(-1)?.nextCursor ?? null;
}

/** The cursor the page before the beginning of the list is asked with, or `null` where the beginning has been reached. */
export function cursorBefore(held: HeldTimeline): string | null {
    return held.slots.at(0)?.previousCursor ?? null;
}

/** Where in the list a row is, as the page holding it, how that page is read, and the row's place inside it. */
export interface HeldPosition {
    readonly cursor: string | null;
    readonly readAs: MailTimelinePageDirection;
    readonly rowInPage: number;
}

/**
 * Which page holds a row and where in it, which is what a returning visit is read back from.
 *
 * The direction travels with the cursor because the pair is what names a page: the same cursor read the other way
 * answers with the page on the other side of it, which would put a returning reader a page from where they left.
 */
export function positionOfRow(held: HeldTimeline, row: number): HeldPosition | null {
    let passed = 0;

    for (const slot of held.slots) {
        if (row < passed + slot.rowCount) {
            return { cursor: slot.askedWith, readAs: slot.readAs, rowInPage: row - passed };
        }

        passed += slot.rowCount;
    }

    return null;
}

/** The row the first row of a page stands at, or `null` where the list knows no such page. */
export function rowOfSlot(held: HeldTimeline, slot: number): number | null {
    if (slot < 0 || slot >= held.slots.length) {
        return null;
    }

    return held.slots.slice(0, slot).reduce((rows, passed) => rows + passed.rowCount, 0);
}

/** What one read asked for, which is what says where its answer belongs. */
export interface TimelineRead {
    readonly cursor: string | null;
    readonly direction: MailTimelinePageDirection;

    /** The page whose dropped rows this read is fetching again, or `null` where it extends one end of the list. */
    readonly refilling: number | null;
}

/** The page a read the list has not made yet would ask for, or `null` where what is on the screen is all held. */
export function wantedFor(held: HeldTimeline, firstRow: number, lastRow: number): TimelineRead | null {
    let passed = 0;

    for (const [at, slot] of held.slots.entries()) {
        const beyond = passed + slot.rowCount;

        if (slot.emails === null && passed <= lastRow && beyond > firstRow) {
            return { cursor: slot.askedWith, direction: slot.readAs, refilling: at };
        }

        passed += slot.rowCount;
    }

    const after = cursorAfter(held);
    if (after !== null && lastRow >= passed - 1) {
        return { cursor: after, direction: 'forward', refilling: null };
    }

    const before = cursorBefore(held);
    if (before !== null && firstRow <= 0) {
        return { cursor: before, direction: 'backward', refilling: null };
    }

    return null;
}

/** The page a read answered with, as a slot the list stands on. */
function slotFor(page: MailTimelinePage, read: TimelineRead): TimelineSlot {
    return {
        askedWith: read.cursor,
        readAs: read.direction,
        rowCount: page.emails.length,
        emails: page.emails,
        nextCursor: page.nextCursor,
        previousCursor: page.previousCursor,
    };
}

/**
 * What the list knows once the deployment said mail arrived at the end this list is read from.
 *
 * The leading page's rows are dropped rather than read again here, because dropping is what makes the re-read
 * conditional: `wantedFor` asks for a dropped page only while it is on the screen, so a reader who has scrolled away
 * keeps every row they are looking at and the page comes back when they come back to it.
 *
 * @param held What the list knows now.
 * @returns What the list knows.
 */
export function arrivalNoticed(held: HeldTimeline): HeldTimeline {
    return held.slots.length === 0
        ? held
        : { slots: held.slots.map((slot, at) => (at === 0 ? { ...slot, emails: null } : slot)) };
}

/**
 * What the list knows once the deployment named rows whose mail is no longer what was drawn.
 *
 * Only the pages actually holding one of the named rows are dropped, on the same rule: what a reader is looking at is
 * read again while they are looking at it, and what they are not stays where it is until they reach it.
 *
 * @param held What the list knows now.
 * @param storedEmailIds The rows the deployment named.
 * @returns What the list knows.
 */
export function changeNoticed(held: HeldTimeline, storedEmailIds: readonly string[]): HeldTimeline {
    const named = new Set(storedEmailIds);

    if (named.size === 0) {
        return held;
    }

    const holdsNamed = (slot: TimelineSlot): boolean => slot.emails?.some((email) => named.has(email.id)) === true;

    // Answered before anything is rebuilt, so a change naming mail this list is not holding leaves the list the object
    // it already was — which is what keeps a signal about another folder from re-rendering every row of this one.
    if (!held.slots.some(holdsNamed)) {
        return held;
    }

    return { slots: held.slots.map((slot) => (holdsNamed(slot) ? { ...slot, emails: null } : slot)) };
}

/**
 * What the list knows once a page has answered.
 *
 * A page that was asked for to refill a dropped one goes back where it stood; one that extends the list joins the end
 * it was read from.
 *
 * @param held What the list knows now.
 * @param page The page that answered.
 * @param read What was asked for, which says where the answer belongs.
 * @returns What the list knows.
 */
export function answered(held: HeldTimeline, page: MailTimelinePage, read: TimelineRead): HeldTimeline {
    if (read.refilling !== null) {
        return {
            slots: held.slots.map((slot, at) => (at === read.refilling ? slotFor(page, read) : slot)),
        };
    }

    // A page with no rows is the end of the list having been reached between two reads, and it joins nothing rather
    // than standing as a row of nowhere. The first page is the exception: a folder holding no mail answers with one,
    // and a list that recorded nothing would ask for it again on every render.
    if (page.emails.length === 0 && held.slots.length > 0) {
        return endReached(held, read.direction);
    }

    const slot = slotFor(page, read);

    return { slots: read.direction === 'forward' ? [...held.slots, slot] : [slot, ...held.slots] };
}

// An end that answered with no rows is an end reached, and the cursor pointing at it is retired so the list does not
// ask again on the next scroll. Nothing else about the slot changes: its rows and its other cursor are still what they
// were.
function endReached(held: HeldTimeline, direction: MailTimelinePageDirection): HeldTimeline {
    const at = direction === 'forward' ? held.slots.length - 1 : 0;

    return {
        slots: held.slots.map((slot, passed) =>
            passed === at
                ? { ...slot, ...(direction === 'forward' ? { nextCursor: null } : { previousCursor: null }) }
                : slot,
        ),
    };
}

/**
 * The list with the rows of every page too far from the reader dropped.
 *
 * The slots stay: each keeps its height and the cursor its rows are read back with, so dropping costs the reader
 * nothing until they scroll back into one, and costs them one page read when they do.
 *
 * @param held What the list knows.
 * @param firstRow The first row on the screen.
 * @param lastRow The last row on the screen.
 * @returns The list with distant rows dropped, or the same list where nothing was far enough to drop.
 */
export function trimmedAround(held: HeldTimeline, firstRow: number, lastRow: number): HeldTimeline {
    const reached: number[] = [];
    let passed = 0;

    for (const [at, slot] of held.slots.entries()) {
        if (passed <= lastRow && passed + slot.rowCount > firstRow) {
            reached.push(at);
        }

        passed += slot.rowCount;
    }

    const nearest = reached[0] ?? 0;
    const furthest = reached.at(-1) ?? held.slots.length - 1;
    const keptFrom = nearest - pagesKeptEitherSide;
    const keptTo = furthest + pagesKeptEitherSide;

    const slots = held.slots.map((slot, at) =>
        slot.emails === null || (at >= keptFrom && at <= keptTo) ? slot : { ...slot, emails: null },
    );

    // The list it was given where nothing was far enough to drop, so a scroll that changes nothing renders nothing.
    return slots.some((slot, at) => slot !== held.slots[at]) ? { slots } : held;
}
