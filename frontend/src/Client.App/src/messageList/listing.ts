// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    longestTimelinePage,
    type MailTimelineOrder,
    type MailTimelinePageDirection,
    type MailTimelineQuery,
} from '@mailfathom/client-backend';
import { namedInScope, scopePointsAtJunk, type MailScope } from '../workspace/mailScope';

// What the reader has asked of one folder: which way round it is read, and what is kept out of it. It is the list's own
// state rather than the workspace's, because it belongs to a folder rather than to the person — moving to another
// mailbox is not a reason to inherit the last one's filters, and coming back to this one is a reason to find them.
//
// It is also half of what a cursor means. The deployment refuses a cursor presented under different filters or a
// different order, which is why `rememberedListings.ts` keeps the cursor inside the same record as the two of them:
// changing either replaces the record, and there is nowhere for a cursor issued under the old one to survive.

/** The spans of time the list offers as one press, each reckoned from the reader's own day rather than from a server's. */
export type MailListDateRange = 'today' | 'lastSevenDays' | 'lastThirtyDays' | 'thisYear';

/** Every span the list offers, in the order they are drawn. */
export const dateRanges: readonly MailListDateRange[] = ['today', 'lastSevenDays', 'lastThirtyDays', 'thisYear'];

/** What the list is keeping out, where each of the three keeps both answers unless the reader narrowed it. */
export interface MailListFilters {
    readonly unread: boolean | null;
    readonly flagged: boolean | null;
    readonly hasAttachments: boolean | null;

    /** Whether the junk folder takes part in a list spanning folders, which it does not unless the reader asks. */
    readonly includeJunk: boolean;

    /**
     * Which offered span {@link receivedFrom} was set from, or `null` where the reader typed the pair themselves.
     *
     * The span is resolved to an instant the moment it is picked rather than each time the list is read, which is what
     * keeps a cursor usable: the deployment refuses one presented under different filters, and a span reckoned afresh
     * on every page would silently become a different filter at midnight, halfway down a folder somebody was reading.
     */
    readonly dateRange: MailListDateRange | null;

    /** The start of the received range as a local `YYYY-MM-DDTHH:mm`, inclusive, or `null` for no start. */
    readonly receivedFrom: string | null;

    /** The end of the received range as a local `YYYY-MM-DDTHH:mm`, exclusive, or `null` for no end. */
    readonly receivedTo: string | null;
}

/** How one folder is being read. */
export interface MailListing {
    readonly order: MailTimelineOrder;
    readonly filters: MailListFilters;
}

/** What a folder nobody has narrowed is read as: newest first, nothing kept out, and no junk swept in. */
export const openingListing: MailListing = {
    order: 'newestFirst',
    filters: {
        unread: null,
        flagged: null,
        hasAttachments: null,
        includeJunk: false,
        dateRange: null,
        receivedFrom: null,
        receivedTo: null,
    },
};

/**
 * How many rows one page holds.
 *
 * The largest the deployment serves, because a page is one exchange and the list holds a bounded number of them: a
 * smaller page would cost the reader more round trips for the same rows and would bound the list's memory no further.
 */
export const rowsPerPage = longestTimelinePage;

/**
 * The request one page of a scope is read with.
 *
 * A scope pointing at junk asks for it whatever the reader's filter says, and that is narrowing rather than widening —
 * {@link scopePointsAtJunk} holds the reasoning.
 *
 * @param scope What the client is looking at.
 * @param listing How the reader has asked for it to be read.
 * @param cursor The cursor the page is asked from, or `null` for the leading end of the list.
 * @param direction Which way the page continues from that cursor.
 * @returns The request the client surface is asked with.
 */
export function queryFor(
    scope: MailScope,
    listing: MailListing,
    cursor: string | null,
    direction: MailTimelinePageDirection,
): MailTimelineQuery {
    const { account, folder } = namedInScope(scope);

    return {
        account,
        folder,
        includeJunk: listing.filters.includeJunk || scopePointsAtJunk(scope),
        unread: listing.filters.unread,
        flagged: listing.filters.flagged,
        hasAttachments: listing.filters.hasAttachments,
        receivedOnOrAfter: instantAt(listing.filters.receivedFrom),
        receivedBefore: instantAt(listing.filters.receivedTo),
        order: listing.order,
        direction,
        pageSize: rowsPerPage,
        cursor,
    };
}

/** Whether the reader has narrowed the list at all, which is what says a folder is empty rather than filtered to nothing. */
export function narrowed(filters: MailListFilters): boolean {
    return (
        filters.unread !== null ||
        filters.flagged !== null ||
        filters.hasAttachments !== null ||
        filters.receivedFrom !== null ||
        filters.receivedTo !== null
    );
}

/**
 * How many narrowings are in force, which is the number the filter control carries.
 *
 * The received range counts once however it was set, because a reader took one decision about when mail arrived rather
 * than two about a pair of fields. Reading the mail the folder holds in another order is counted with them: it is not a
 * narrowing, but it is a reason the message somebody expected at the top is not there, which is the same surprise the
 * count exists to answer. Including junk is deliberately not counted — it widens the list, so it can never be why a
 * folder looks emptier than the reader expects.
 *
 * @param listing How the reader has asked the folder to be read.
 * @returns The count of narrowings and orderings the reader has chosen.
 */
export function narrowingsInForce(listing: MailListing): number {
    const chosen = [
        listing.filters.unread !== null,
        listing.filters.flagged !== null,
        listing.filters.hasAttachments !== null,
        listing.filters.receivedFrom !== null || listing.filters.receivedTo !== null,
        listing.order !== openingListing.order,
    ];

    return chosen.filter(Boolean).length;
}

/**
 * The filters a folder is read with once one of the offered spans of time is picked.
 *
 * Each span begins at the start of a day in the reader's own zone and runs to now, so "the last seven days" is seven of
 * their days rather than a hundred and sixty-eight hours ending mid-afternoon. That also keeps the filter still while
 * somebody reads: a span reckoned from the current instant would be a different filter on every page.
 *
 * @param filters What the list is narrowed to now.
 * @param range The span the reader picked, or `null` to stop narrowing by when mail arrived.
 * @param now The instant the reader picked it at.
 * @returns The filters with that span in force and any typed pair replaced by it.
 */
export function narrowedToRange(filters: MailListFilters, range: MailListDateRange | null, now: Date): MailListFilters {
    if (range === null) {
        return { ...filters, dateRange: null, receivedFrom: null, receivedTo: null };
    }

    return { ...filters, dateRange: range, receivedFrom: localMinute(startOf(range, now)), receivedTo: null };
}

/**
 * Whether a typed range can select anything, which is what says the pair may be asked for.
 *
 * A range whose end falls before its start selects nothing and the deployment refuses it rather than answering an empty
 * page, so the screen says so where the reader can see which of the two to move.
 *
 * @param from The start of the range as the reader typed it, or `null`.
 * @param to The end of the range as the reader typed it, or `null`.
 * @returns Whether mail could have arrived between the two.
 */
export function selectableRange(from: string | null, to: string | null): boolean {
    return from === null || to === null || from <= to;
}

// A local wall-clock minute turned into the instant it names, so that the moment somebody picked is their moment rather
// than the one a server in another zone would have read. A value the control never produced is answered as no bound.
function instantAt(local: string | null): string | null {
    if (local === null || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(local)) {
        return null;
    }

    const at = new Date(local);

    return Number.isNaN(at.getTime()) ? null : at.toISOString();
}

// Where each offered span begins, in the reader's own calendar rather than in an arithmetic on milliseconds: a day is
// not always twenty-four hours long, and the day a clock change falls on is exactly when that matters.
function startOf(range: MailListDateRange, now: Date): Date {
    switch (range) {
        case 'today':
            return new Date(now.getFullYear(), now.getMonth(), now.getDate());
        case 'lastSevenDays':
            return new Date(now.getFullYear(), now.getMonth(), now.getDate() - 6);
        case 'lastThirtyDays':
            return new Date(now.getFullYear(), now.getMonth(), now.getDate() - 29);
        case 'thisYear':
            return new Date(now.getFullYear(), 0, 1);
    }
}

// The spelling the date control reads and writes, which is local wall-clock time with no zone on it.
function localMinute(at: Date): string {
    const padded = (value: number): string => String(value).padStart(2, '0');

    return `${String(at.getFullYear()).padStart(4, '0')}-${padded(at.getMonth() + 1)}-${padded(at.getDate())}T${padded(at.getHours())}:${padded(at.getMinutes())}`;
}
