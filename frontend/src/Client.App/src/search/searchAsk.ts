// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { longestSearchPage, type MailSearchQuery } from '@mailfathom/client-backend';
import { namedInScope, scopePointsAtJunk, type MailScope } from '../workspace/mailScope';

// What a search is: the words that rank, and every filter that constrains. It is one value rather than a text field
// beside a bag of state, because the whole of it is what the answer belongs to — a cursor is issued under it, the
// results are keyed by it, and a filter changed while results are on the screen has started a different search rather
// than adjusted the one there.
//
// Each filter is a value that is either in force or absent, and never a third thing. That is what makes them the
// removable objects this screen owes a reader: a filter in force is drawn, and removing it is one press that clears
// one field. `unread`, `flagged`, and `hasAttachments` therefore hold `true` or nothing rather than three states, for
// the reason `messageList/ListSettings.tsx` gives about the folder's own narrowings — "read", "unflagged", and
// "without attachments" are not searches anybody asks for.
//
// The two dates are calendar days rather than instants, because a person picks a day and the browser's own date
// control answers one. Turning a day into the half-open range the route takes is this module's, and it is done in the
// reader's own zone: somebody who asked for the fifteenth means their fifteenth.

/** The words a search ranks by, and every filter in force. */
export interface MailSearchAsk {
    /** What a person typed, which is the one part a search cannot be made without. */
    readonly text: string;

    /** The account searched, by the identifier the accounts route names it with, or `null` for every account. */
    readonly account: string | null;

    /** The folder searched, by its alias or as `role:Inbox`, or `null` for every folder. */
    readonly folder: string | null;

    /** Whether the junk folder takes part, which it does not unless the search says so. */
    readonly includeJunk: boolean;

    /** The address the sender must carry, or `null` for any sender. */
    readonly sender: string | null;

    /** The address a recipient must carry, or `null` for any recipient. */
    readonly recipient: string | null;

    /** The first calendar day the search reaches back to, as `yyyy-mm-dd`, or `null` for no start. */
    readonly receivedFrom: string | null;

    /** The last calendar day the search reaches, inclusive, as `yyyy-mm-dd`, or `null` for no end. */
    readonly receivedTo: string | null;

    readonly unread: true | null;
    readonly flagged: true | null;
    readonly hasAttachments: true | null;
}

/**
 * One filter, named so that a chip and the press that removes it are one thing rather than ten.
 *
 * Exhaustive by its own type, so a filter added to the ask fails to compile until it has a name, a label, and a way
 * to be taken off again.
 */
export type MailSearchNarrowing =
    | 'account'
    | 'folder'
    | 'sender'
    | 'recipient'
    | 'receivedFrom'
    | 'receivedTo'
    | 'unread'
    | 'flagged'
    | 'hasAttachments'
    | 'includeJunk';

/**
 * How many results one page holds.
 *
 * The largest the deployment serves, for the reason the list asks for its largest page: a page is one exchange, the
 * ranked list is short enough to hold whole, and a smaller page would cost more round trips for the same results.
 */
export const resultsPerPage = longestSearchPage;

// The order the filters are drawn in, which is where the scope, who wrote, when, and what state the message is in fall
// for somebody reading them left to right. Stated once so the chips and anything else listing them cannot disagree.
const narrowingOrder: readonly MailSearchNarrowing[] = [
    'account',
    'folder',
    'includeJunk',
    'sender',
    'recipient',
    'receivedFrom',
    'receivedTo',
    'unread',
    'flagged',
    'hasAttachments',
];

/**
 * The search somebody starts by typing into the field, which is the scope they are looking at with nothing else on it.
 *
 * The scope is copied into the search rather than read from the workspace on every request, which is what lets it be
 * shown and taken off: widening a search is removing a filter here, and it leaves the mailbox they are looking at
 * exactly where it was.
 *
 * @param scope What the client is looking at.
 * @param text What the person typed.
 * @returns The search to ask with.
 */
export function askIn(scope: MailScope, text: string): MailSearchAsk {
    const { account, folder } = namedInScope(scope);

    return {
        text,
        account,
        folder,
        includeJunk: scopePointsAtJunk(scope),
        sender: null,
        recipient: null,
        receivedFrom: null,
        receivedTo: null,
        unread: null,
        flagged: null,
        hasAttachments: null,
    };
}

/** Which filters are in force, in the order they are drawn. */
export function narrowings(ask: MailSearchAsk): readonly MailSearchNarrowing[] {
    return narrowingOrder.filter((narrowing) => inForce(ask, narrowing));
}

/** Whether one named filter is in force. */
export function inForce(ask: MailSearchAsk, narrowing: MailSearchNarrowing): boolean {
    return narrowing === 'includeJunk' ? ask.includeJunk : ask[narrowing] !== null;
}

/** What one filter is set to, for a chip that has to say which account, which address, or which day. */
export function valueOf(ask: MailSearchAsk, narrowing: MailSearchNarrowing): string | null {
    switch (narrowing) {
        case 'account':
            return ask.account;
        case 'folder':
            return ask.folder;
        case 'sender':
            return ask.sender;
        case 'recipient':
            return ask.recipient;
        case 'receivedFrom':
            return ask.receivedFrom;
        case 'receivedTo':
            return ask.receivedTo;
        case 'includeJunk':
        case 'unread':
        case 'flagged':
        case 'hasAttachments':
            return null;
    }
}

/** The same search with one filter taken off, which is what widening one press at a time is. */
export function without(ask: MailSearchAsk, narrowing: MailSearchNarrowing): MailSearchAsk {
    switch (narrowing) {
        case 'account':
            return { ...ask, account: null };
        case 'folder':
            return { ...ask, folder: null };
        case 'sender':
            return { ...ask, sender: null };
        case 'recipient':
            return { ...ask, recipient: null };
        case 'receivedFrom':
            return { ...ask, receivedFrom: null };
        case 'receivedTo':
            return { ...ask, receivedTo: null };
        case 'unread':
            return { ...ask, unread: null };
        case 'flagged':
            return { ...ask, flagged: null };
        case 'hasAttachments':
            return { ...ask, hasAttachments: null };
        case 'includeJunk':
            return { ...ask, includeJunk: false };
    }
}

/** The same words with every filter taken off, which is what an empty result offers instead of a blank pane. */
export function widened(ask: MailSearchAsk): MailSearchAsk {
    return narrowings(ask).reduce(without, ask);
}

/**
 * What identifies a search, which is what its results are keyed by.
 *
 * A search whose text or whose filters changed is a different ranked list read from its own cursor, so keying the
 * results by this is what makes changing either start a search rather than reconcile one.
 */
export function askKey(ask: MailSearchAsk): string {
    return JSON.stringify([
        ask.text,
        ask.account,
        ask.folder,
        ask.includeJunk,
        ask.sender,
        ask.recipient,
        ask.receivedFrom,
        ask.receivedTo,
        ask.unread,
        ask.flagged,
        ask.hasAttachments,
    ]);
}

/**
 * The request one page of a search is read with.
 *
 * @param ask The words and the filters in force.
 * @param cursor The cursor the page continues from, or `null` for the best-ranked results.
 * @returns The request the client surface is asked with.
 */
export function queryFor(ask: MailSearchAsk, cursor: string | null): MailSearchQuery {
    return {
        text: ask.text,
        account: ask.account,
        folder: ask.folder,
        includeJunk: ask.includeJunk,
        sender: ask.sender,
        recipient: ask.recipient,
        unread: ask.unread,
        flagged: ask.flagged,
        hasAttachments: ask.hasAttachments,
        receivedOnOrAfter: dayBoundary(ask.receivedFrom, 0),
        receivedBefore: dayBoundary(ask.receivedTo, 1),
        pageSize: resultsPerPage,
        cursor,
    };
}

/**
 * Whether the reader typed something a search can be made of, which is what says the field may be submitted.
 *
 * Blank text is the one filter that cannot be absent, and the deployment refuses a search without it — so the field
 * says so rather than sending one and reporting a refusal as a defect.
 */
export function askable(text: string, longest: number): boolean {
    const typed = text.trim();

    return typed.length > 0 && typed.length <= longest;
}

/**
 * Whether the value can be used as the address filter it is being added as.
 *
 * The deployment judges what an address is, and this does not attempt to: it refuses only what could not be one at
 * all, so that somebody who typed a word into the sender field is told which field to correct rather than being
 * handed a refusal the route would have answered.
 */
export function addressFilter(value: string): string | null {
    const typed = value.trim();
    const at = typed.indexOf('@');

    if (at <= 0 || at === typed.length - 1 || typed.length > longestAddressFilter) {
        return null;
    }

    return /\s/.test(typed) ? null : typed;
}

// The longest address this client will filter by, which is the longest an address is allowed to be.
const longestAddressFilter = 320;

/**
 * Whether a range of days can select anything, which is what says the two date fields may be submitted together.
 *
 * A range whose end falls before its start selects nothing, and the deployment refuses it rather than answering an
 * empty page — so the screen refuses it first, where the reader can see which of the two to move.
 */
export function selectableRange(from: string | null, to: string | null): boolean {
    return from === null || to === null || from <= to;
}

// A calendar day turned into the instant it begins at in the reader's own zone, so that "the fifteenth" is their
// fifteenth rather than the one a server in another zone would have read. The offset is what makes the end of a range
// inclusive: the day after the last one, at its start, is where a half-open range ends.
function dayBoundary(day: string | null, days: number): string | null {
    if (day === null) {
        return null;
    }

    const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);

    if (parts === null) {
        return null;
    }

    const at = new Date(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]) + days);

    return Number.isNaN(at.getTime()) ? null : at.toISOString();
}
