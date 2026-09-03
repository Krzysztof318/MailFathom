// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailTimelineOrder, MailTimelinePageDirection } from '@mailfathom/client-backend';
import { scopeKey, type MailScope } from '../workspace/mailScope';
import {
    dateRanges,
    openingListing,
    rowsPerPage,
    type MailListDateRange,
    type MailListFilters,
    type MailListing,
} from './listing';

// Where a folder's reading position survives leaving it and reloading. Outside React deliberately: the position moves
// while somebody scrolls, and holding it in state would re-render everything under the workspace provider on the one
// interaction the whole of this screen exists to keep smooth.
//
// What is kept is where the reader was and how they were reading, and nothing about any message: a cursor the
// deployment issued, a row number inside the page it names, the order, and the filters. No subject, no sender, and no
// identity of anything in the folder — a store is a place a screen's contents must not accumulate in.
//
// Four of the five things the position is keyed by are in the key itself: the deployment's address and the scope, which
// names the account and the folder. The fifth pair — the order and the filters — is inside the record rather than in
// the key, which is the stronger arrangement: a cursor and the two values it was issued under are written and replaced
// together, so a cursor cannot outlive them and there is no composite key to get wrong. The owner is the credential,
// and `forgetListings` is called wherever the client lets one go.
//
// The session's store rather than the machine's, and reached as `window.sessionStorage` rather than as the bare global,
// both for the reasons `workspace/rememberedWorkspace.ts` gives.
const storageKey = 'mailfathom.listings';

// How many folders keep a position before the oldest is dropped. A reader moves between a handful of mailboxes in a
// session; this is far above that and bounds what one tab can accumulate.
const mostRememberedListings = 64;

// The longest cursor read back. The client surface's own bound is smaller; this is what a store may hold before what is
// in it is read as somebody's writing rather than as this client's.
const longestCursor = 4_096;

/** Where a folder was left, and how it was being read at the time. */
export interface RememberedListing extends MailListing {
    /** The cursor of the page the reader's leading row was in, or `null` where it was the leading page. */
    readonly cursor: string | null;

    /** Which way that page was read from the cursor, which is the other half of reading it again. */
    readonly readAs: MailTimelinePageDirection;

    /** Which row of that page was under the top of the scroller. */
    readonly rowInPage: number;
}

/**
 * Where a folder nobody has opened yet is read from: the opening listing, at the leading end of it.
 *
 * Named for the reading position rather than for a message state, because `unread` is this client's word for a message
 * nobody has read and is a filter three controls away from here.
 */
export const neverOpenedListing: RememberedListing = {
    ...openingListing,
    cursor: null,
    readAs: 'forward',
    rowInPage: 0,
};

/** How a folder of one deployment was last read, or the opening listing where it has not been. */
export function rememberedListing(baseAddress: string, scope: MailScope): RememberedListing {
    return listingsIn(stored())[keyFor(baseAddress, scope)] ?? neverOpenedListing;
}

/** Keeps how a folder is being read and where in it the reader is, so leaving and returning is a continuation. */
export function rememberListing(baseAddress: string, scope: MailScope, listing: RememberedListing): void {
    const key = keyFor(baseAddress, scope);

    // Written back last so that it is the newest entry, which is what makes the oldest the one dropped: object key
    // order is insertion order for string keys, and re-reading a folder therefore keeps it.
    const entries = Object.entries(listingsIn(stored()))
        .filter(([held]) => held !== key)
        .slice(-(mostRememberedListings - 1));

    write(Object.fromEntries([...entries, [key, listing]]));
}

/**
 * Drops every remembered position.
 *
 * Called wherever the client lets a credential or a deployment go, which is what keeps a position from outliving the
 * person it belonged to. A cursor is not mail, but where somebody was reading is theirs.
 */
export function forgetListings(): void {
    try {
        window.sessionStorage.removeItem(storageKey);
    } catch {
        // A browser refusing storage has nothing kept to remove.
    }
}

function keyFor(baseAddress: string, scope: MailScope): string {
    return `${baseAddress}\n${scopeKey(scope)}`;
}

function stored(): unknown {
    let kept: string | null;

    try {
        kept = window.sessionStorage.getItem(storageKey);
    } catch {
        return null;
    }

    if (kept === null) {
        return null;
    }

    try {
        return JSON.parse(kept);
    } catch {
        return null;
    }
}

function write(listings: Readonly<Record<string, RememberedListing>>): void {
    try {
        window.sessionStorage.setItem(storageKey, JSON.stringify(listings));
    } catch {
        // A browser refusing storage still runs the client; a folder then opens at its leading end, which is a smaller
        // loss than a client that fails over a convenience.
    }
}

// Read back as untrusted input, because a store is a place a person can write. A record with anything wrong in it is
// answered as nothing kept, so a cursor this client did not issue never reaches the deployment.
function listingsIn(value: unknown): Readonly<Record<string, RememberedListing>> {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return {};
    }

    const entries = Object.entries(value as Record<string, unknown>);
    if (entries.length > mostRememberedListings) {
        return {};
    }

    const listings: Record<string, RememberedListing> = {};
    for (const [key, kept] of entries) {
        const listing = listingIn(kept);

        if (listing === null) {
            return {};
        }

        listings[key] = listing;
    }

    return listings;
}

function listingIn(value: unknown): RememberedListing | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return null;
    }

    const record = value as Record<string, unknown>;
    const order = record['order'];
    const filters = filtersIn(record['filters']);
    const cursor = record['cursor'] ?? null;
    const readAs = record['readAs'];
    const rowInPage = record['rowInPage'];

    if (!isOrder(order) || filters === null || !isDirection(readAs)) {
        return null;
    }

    if (cursor !== null && (typeof cursor !== 'string' || cursor.length === 0 || cursor.length > longestCursor)) {
        return null;
    }

    if (typeof rowInPage !== 'number' || !Number.isSafeInteger(rowInPage) || rowInPage < 0) {
        return null;
    }

    // A row past the page it names is a record this client never wrote: the page it would be read back into cannot
    // hold it, and scrolling to it would leave the reader below every row there is.
    return rowInPage >= rowsPerPage ? null : { order, filters, cursor, readAs, rowInPage };
}

function filtersIn(value: unknown): MailListFilters | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return null;
    }

    const record = value as Record<string, unknown>;
    const unread = record['unread'] ?? null;
    const flagged = record['flagged'] ?? null;
    const hasAttachments = record['hasAttachments'] ?? null;
    const includeJunk = record['includeJunk'];
    const dateRange = record['dateRange'] ?? null;
    const receivedFrom = record['receivedFrom'] ?? null;
    const receivedTo = record['receivedTo'] ?? null;

    if (!isWanted(unread) || !isWanted(flagged) || !isWanted(hasAttachments) || typeof includeJunk !== 'boolean') {
        return null;
    }

    if (!isRange(dateRange) || !isMinute(receivedFrom) || !isMinute(receivedTo)) {
        return null;
    }

    // A span resolves to a start and no end the moment it is picked, so a record pairing one with a missing start or
    // with an end is a record this client never wrote. Both are refused rather than read: the panel draws neither
    // field while a span is lit, so a bound arriving that way would narrow the folder where nothing shows it.
    if (dateRange !== null && (receivedFrom === null || receivedTo !== null)) {
        return null;
    }

    return { unread, flagged, hasAttachments, includeJunk, dateRange, receivedFrom, receivedTo };
}

function isWanted(value: unknown): value is boolean | null {
    return value === null || typeof value === 'boolean';
}

function isRange(value: unknown): value is MailListDateRange | null {
    return value === null || (typeof value === 'string' && dateRanges.includes(value as MailListDateRange));
}

// The spelling the date control writes, which is what the filter is asked with. Anything else is somebody's writing
// rather than this client's, and a bound it did not compose never reaches the deployment.
function isMinute(value: unknown): value is string | null {
    return value === null || (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value));
}

function isOrder(value: unknown): value is MailTimelineOrder {
    return value === 'newestFirst' || value === 'oldestFirst';
}

function isDirection(value: unknown): value is MailTimelinePageDirection {
    return value === 'forward' || value === 'backward';
}
