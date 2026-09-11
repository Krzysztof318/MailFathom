// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    MailTimelineEntry,
    MailTimelinePage,
    MailTimelinePageDirection,
    SignalledFlags,
} from '@mailfathom/client-backend';

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

    /**
     * Whether the rows held here may no longer be what the deployment holds, because a refresh asked for everything to
     * be read again. They stay drawn meanwhile, and the page is read again from its own cursor while it is on the screen.
     */
    readonly stale: boolean;

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

/**
 * The list without the rows a person has taken out of this folder, which is what every reading of it is done against.
 *
 * Derived on each render rather than written into what is held, and that is the whole point of it. What is held is what
 * the deployment answered; what is drawn is that, less what somebody has just asked to have filed elsewhere. Removing
 * the rows from the held pages instead would be undone by the next read of one — the deployment goes on answering the
 * pre-change state for as long as its own pass takes — and a refusal would have nothing to put back, whereas letting
 * the act go from the map it is derived from brings the row back where it stood at no cost at all.
 *
 * A page whose rows are dropped keeps its cursors and its place, exactly as a page the list stopped holding does. What
 * shrinks is its row count, so the list is the height of what it draws.
 *
 * @param held What the list knows.
 * @param leaving Whether that message has been asked to leave the folder this row draws it in. It is asked of the row
 * rather than of an identity, because an act takes a message out of the folder it was performed in and out of no
 * other — the same message drawn in the folder it was filed into is a message that has arrived.
 * @returns The list as it is drawn, and the same object where no row is leaving.
 */
export function withoutLeaving(held: HeldTimeline, leaving: (email: MailTimelineEntry) => boolean): HeldTimeline {
    const holdsLeaving = (slot: TimelineSlot): boolean => slot.emails?.some(leaving) === true;

    // Answered before anything is rebuilt, so an act against mail this list is not drawing leaves the list the object it
    // already was — which is what keeps a folder nobody acted in from re-rendering every row.
    if (!held.slots.some(holdsLeaving)) {
        return held;
    }

    return {
        slots: held.slots.map((slot) => {
            if (!holdsLeaving(slot)) {
                return slot;
            }

            const kept = slot.emails?.filter((email) => !leaving(email)) ?? null;

            return { ...slot, rowCount: kept?.length ?? slot.rowCount, emails: kept };
        }),
    };
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

/**
 * The page a read the list has not made yet would ask for, or `null` where what is on the screen is all held and none of
 * it is waiting to be read again.
 */
export function wantedFor(held: HeldTimeline, firstRow: number, lastRow: number): TimelineRead | null {
    let passed = 0;

    for (const [at, slot] of held.slots.entries()) {
        const beyond = passed + slot.rowCount;

        if ((slot.emails === null || slot.stale) && passed <= lastRow && beyond > firstRow) {
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
        stale: false,
    };
}

/**
 * What the list knows once the deployment said mail arrived at the end this list is read from.
 *
 * The leading page is marked rather than dropped, and the marking is what makes the re-read conditional: `wantedFor`
 * asks for a stale page only while it is on the screen, so a reader who has scrolled away keeps every row they are
 * looking at and the page is read again when they come back to it.
 *
 * **Marked rather than dropped, which is the whole of the rule this file states.** Dropping a page's rows turns a
 * hundred rows a reader is looking at into the space they stood in, drawn as *reading this message again* — for a
 * signal saying one message arrived above them. The rows stay, the page is read again underneath, and what the reader
 * is shown changing is whatever the answer actually differs in.
 *
 * @param held What the list knows now.
 * @returns What the list knows, and the list itself where its leading page holds no rows to mark.
 */
export function arrivalNoticed(held: HeldTimeline): HeldTimeline {
    const leading = held.slots[0];

    if (leading === undefined) {
        return held;
    }

    if (leading.emails === null || leading.stale) {
        return held;
    }

    return { slots: held.slots.map((slot, at) => (at === 0 ? { ...slot, stale: true } : slot)) };
}

/**
 * What the list knows once the deployment named rows whose mail is no longer what was drawn.
 *
 * Only the pages actually holding one of the named rows are marked, on the same rule: what a reader is looking at is
 * read again while they are looking at it, and what they are not stays where it is until they reach it.
 *
 * Marked rather than dropped for the reason {@link arrivalNoticed} gives, and the cost of getting it wrong is highest
 * here: a page is a hundred rows and a signal may name one of them, so dropping the page emptied ninety-nine rows
 * nothing had said anything about.
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

    const holdsNamed = (slot: TimelineSlot): boolean =>
        !slot.stale && slot.emails?.some((email) => named.has(email.id)) === true;

    // Answered before anything is rebuilt, so a change naming mail this list is not holding leaves the list the object
    // it already was — which is what keeps a signal about another folder from re-rendering every row of this one. A
    // page already marked is among those: it is being read again already, and marking it twice is the same page.
    if (!held.slots.some(holdsNamed)) {
        return held;
    }

    return { slots: held.slots.map((slot) => (holdsNamed(slot) ? { ...slot, stale: true } : slot)) };
}

/**
 * What the list knows once a refresh asked for everything it holds to be read again.
 *
 * Every page holding rows is marked rather than dropped, which is what separates a refresh from a signal naming mail:
 * the rows stay drawn while their page is read again, so a reader sees rows change rather than the list turning into
 * its own skeleton every five minutes. What is on the screen is read again now and what is not when the reader reaches
 * it, on the rule `arrivalNoticed` states.
 *
 * @param held What the list knows now.
 * @returns What the list knows, and the list itself where it holds no rows.
 */
export function refreshAsked(held: HeldTimeline): HeldTimeline {
    return held.slots.some((slot) => slot.emails !== null)
        ? { slots: held.slots.map((slot) => (slot.emails === null ? slot : { ...slot, stale: true })) }
        : held;
}

/**
 * What the list knows once reading one of its stale pages again was not answered.
 *
 * The rows stay where they are and the page stops being asked for: they are still the truest thing the list has, and
 * the next refresh asks again. Asking again at once would be a request per render against a deployment that is not
 * answering.
 *
 * @param held What the list knows now.
 * @param slot The page the read was refilling.
 * @returns What the list knows.
 */
export function refreshUnanswered(held: HeldTimeline, slot: number): HeldTimeline {
    return { slots: held.slots.map((standing, at) => (at === slot ? { ...standing, stale: false } : standing)) };
}

/**
 * Whether a read refills a page whose rows are still drawn, which is the one read whose failure takes nothing off the
 * screen: only a stale page is read again while it holds its rows.
 */
export function refillsHeldRows(held: HeldTimeline, read: TimelineRead | null): boolean {
    const refilling = read?.refilling ?? null;

    return refilling !== null && (held.slots[refilling]?.emails ?? null) !== null;
}

/**
 * What the list knows once the deployment said where some rows' flags now stand.
 *
 * The rows are redrawn where they are held rather than dropped, which is the whole point of the statement: a star or a
 * read mark lands on the screen without a page being read again, without the reader's place moving, and without the row
 * under their pointer going anywhere. A row the list is not holding is ignored — the page it is on is read again from
 * its own cursor when the reader reaches it, and it will carry the flag by then.
 *
 * @param held What the list knows now.
 * @param flags Where the deployment says each named row's flags stand.
 * @returns What the list knows.
 */
export function flagsNoticed(held: HeldTimeline, flags: readonly SignalledFlags[]): HeldTimeline {
    const stated = new Map(flags.map((flag) => [flag.email, flag]));

    const redrawn = (email: MailTimelineEntry): MailTimelineEntry => {
        const flag = stated.get(email.id);

        if (flag === undefined) {
            return email;
        }

        // A flag the statement is silent about is one the deployment did not observe rather than one it cleared, so the
        // row keeps what it was drawn with. That is also what makes applying the same statement twice the same rows.
        return {
            ...email,
            unread: flag.isSeen === null ? email.unread : !flag.isSeen,
            flagged: flag.isFlagged ?? email.flagged,
        };
    };

    const holdsNamed = (slot: TimelineSlot): boolean => slot.emails?.some((email) => stated.has(email.id)) === true;

    // Answered before anything is rebuilt, so a statement about mail this list is not holding leaves the list the
    // object it already was — the same reason `changeNoticed` asks first. A page holding none of the rows named keeps
    // its own object for that reason one level down.
    if (!held.slots.some(holdsNamed)) {
        return held;
    }

    return {
        slots: held.slots.map((slot) =>
            holdsNamed(slot) ? { ...slot, emails: slot.emails?.map(redrawn) ?? null } : slot,
        ),
    };
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
 * @param leaving What is leaving the list, which the two rows above were counted without.
 * @returns The list with distant rows dropped, or the same list where nothing was far enough to drop.
 */
export function trimmedAround(
    held: HeldTimeline,
    firstRow: number,
    lastRow: number,
    leaving: (email: MailTimelineEntry) => boolean = () => false,
): HeldTimeline {
    // Rows are counted as they are drawn, because that is the numbering the window was worked out in; the slots line up
    // with what is held either way, since leaving never drops one.
    const measured = withoutLeaving(held, leaving);
    const reached: number[] = [];
    let passed = 0;

    for (const [at, slot] of measured.slots.entries()) {
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
