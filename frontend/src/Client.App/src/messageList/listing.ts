// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    longestTimelinePage,
    type MailTimelineOrder,
    type MailTimelinePageDirection,
    type MailTimelineQuery,
} from '@mailfathom/client-backend';
import type { MailScope } from '../workspace/mailScope';

// What the reader has asked of one folder: which way round it is read, and what is kept out of it. It is the list's own
// state rather than the workspace's, because it belongs to a folder rather than to the person — moving to another
// mailbox is not a reason to inherit the last one's filters, and coming back to this one is a reason to find them.
//
// It is also half of what a cursor means. The deployment refuses a cursor presented under different filters or a
// different order, which is why `rememberedListings.ts` keeps the cursor inside the same record as the two of them:
// changing either replaces the record, and there is nowhere for a cursor issued under the old one to survive.

/** What the list is keeping out, where each of the three keeps both answers unless the reader narrowed it. */
export interface MailListFilters {
    readonly unread: boolean | null;
    readonly flagged: boolean | null;
    readonly hasAttachments: boolean | null;

    /** Whether the junk folder takes part in a list spanning folders, which it does not unless the reader asks. */
    readonly includeJunk: boolean;
}

/** How one folder is being read. */
export interface MailListing {
    readonly order: MailTimelineOrder;
    readonly filters: MailListFilters;
}

/** What a folder nobody has narrowed is read as: newest first, nothing kept out, and no junk swept in. */
export const openingListing: MailListing = {
    order: 'newestFirst',
    filters: { unread: null, flagged: null, hasAttachments: null, includeJunk: false },
};

/**
 * How many rows one page holds.
 *
 * The largest the deployment serves, because a page is one exchange and the list holds a bounded number of them: a
 * smaller page would cost the reader more round trips for the same rows and would bound the list's memory no further.
 */
export const rowsPerPage = longestTimelinePage;

/** The account and the folder a scope names on the client surface, where it names either. */
function askedOf(scope: MailScope): { readonly account: string | null; readonly folder: string | null } {
    switch (scope.kind) {
        case 'everything':
            return { account: null, folder: null };
        case 'role':
            return { account: null, folder: `role:${scope.role}` };
        case 'account':
            return { account: scope.accountId, folder: null };
        case 'folder':
            return { account: scope.accountId, folder: scope.alias };
    }
}

/**
 * Whether the scope is junk the reader pointed at, which is the one case a read asks for junk without being told to.
 *
 * The deployment withholds junk from a read spanning folders, so a reader who has opened their junk folder — or the
 * role that is every account's junk folder at once — would be shown an empty one. Both of those have already excluded
 * everything but junk, so asking cannot reach anything the reader did not point at. Every other role spans many
 * folders across many accounts and is exactly the list junk is withheld from, so it is left out there.
 */
function pointsAtJunk(scope: MailScope): boolean {
    return scope.kind === 'folder' || (scope.kind === 'role' && scope.role === 'Junk');
}

/**
 * The request one page of a scope is read with.
 *
 * A scope pointing at junk asks for it whatever the reader's filter says, and that is narrowing rather than widening —
 * {@link pointsAtJunk} holds the reasoning.
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
    const { account, folder } = askedOf(scope);

    return {
        account,
        folder,
        includeJunk: listing.filters.includeJunk || pointsAtJunk(scope),
        unread: listing.filters.unread,
        flagged: listing.filters.flagged,
        hasAttachments: listing.filters.hasAttachments,
        order: listing.order,
        direction,
        pageSize: rowsPerPage,
        cursor,
    };
}

/** Whether the reader has narrowed the list at all, which is what says a folder is empty rather than filtered to nothing. */
export function narrowed(filters: MailListFilters): boolean {
    return filters.unread !== null || filters.flagged !== null || filters.hasAttachments !== null;
}
