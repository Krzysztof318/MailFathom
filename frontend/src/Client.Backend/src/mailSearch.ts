// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, read, type ClientFailureReason, type ClientResult } from './failure';
import { asRecord } from './json';
import { parseTimelineEntry, type MailTimelineEntry } from './mailTimeline';
import { headersFor, routeFor, type ClientSession } from './session';
import { send, type MailFathomTransport } from './transport';

// One page of the owner's mail ranked against what they are looking for. It is one route rather than two because
// finding a message is one question: somebody who cannot remember whether the words they have are the words the
// message used should not be asked to choose between a word search and a meaning search, so the deployment ranks both
// ways wherever it can and says in the answer which of them happened.
//
// What this package owes the screen above it is therefore three things. The request, whose every field beside the text
// constrains rather than ranks. The row, which is the list's row with two fields added, so one layout draws both. And
// the two fields describing retrieval, which are what keeps a narrower answer from being a quieter one — a page ranked
// by words alone on a deployment that embeds nothing and one whose provider is refusing look identical from the
// results, and only the second is something to act on.

/** The route one page of ranked results is served at, relative to the client prefix. */
export const mailSearchRoute = '/emails/search';

/** Which ranking found a result. */
export type MailSearchRanking = 'LexicalRanking' | 'SemanticRanking' | 'BothRankings';

/** How a page was ranked: by words alone, or by words and meaning together. */
export type MailSearchRetrieval = 'Lexical' | 'Hybrid';

/**
 * What ranking by meaning can do on this deployment.
 *
 * The three are separated because two of them produce the same page and only one of them is a fault: `Inactive` is a
 * deployment that embeds nothing by choice, `Degraded` is one whose provider is refusing, and `Available` is one
 * ranking by meaning.
 */
export type MailSemanticSearch = 'Inactive' | 'Available' | 'Degraded';

/** What a search is asked with, in full: the text that ranks, and the filters that constrain. */
export interface MailSearchQuery {
    /** The text to search for, which every search carries and which no search may leave blank. */
    readonly text: string;

    /** The account to search, by its identifier or its display name, or `null` for every account the owner owns. */
    readonly account: string | null;

    /** The folder to search, by its alias or as `role:Inbox`, or `null` for every folder. */
    readonly folder: string | null;

    /** Whether the junk folder takes part, which it does not unless the request asks. */
    readonly includeJunk: boolean;

    /** The address the sender must carry, or `null` for any sender. */
    readonly sender: string | null;

    /** The address a `To` or `Cc` recipient must carry, or `null` for any recipient. */
    readonly recipient: string | null;

    /** Keep only unread mail, only read mail, or `null` for both. */
    readonly unread: boolean | null;

    /** Keep only flagged mail, only unflagged mail, or `null` for both. */
    readonly flagged: boolean | null;

    /** Keep only mail with attachments, only mail without, or `null` for both. */
    readonly hasAttachments: boolean | null;

    /** The inclusive start of the received range as an instant, or `null` for no start. */
    readonly receivedOnOrAfter: string | null;

    /** The exclusive end of the received range as an instant, or `null` for no end. */
    readonly receivedBefore: string | null;

    /** How many results the page may hold, between one and {@link longestSearchPage}. */
    readonly pageSize: number;

    /** The cursor a previous page answered with, or `null` for the best-ranked results. */
    readonly cursor: string | null;
}

/** One result: the row a list draws, and what says why the row is there. */
export interface MailSearchResult extends MailTimelineEntry {
    /**
     * The extracts around what matched, each marking the matched words with `**`.
     *
     * Text cut from untrusted mail rather than markup to render, and empty for a message that matched on its headers
     * or by meaning alone — which {@link MailSearchResult.matchedBy} is what tells apart.
     */
    readonly snippets: readonly string[];

    /** Which ranking found this result. */
    readonly matchedBy: MailSearchRanking;
}

/** One page of ranked results, and what says how it was ranked. */
export interface MailSearchPage {
    readonly results: readonly MailSearchResult[];

    /** The cursor the following page is asked with, or `null` where the ranked list ends here. */
    readonly nextCursor: string | null;

    /** How many results the read ran under, which is what the request asked for. */
    readonly pageSize: number;

    readonly retrievalMode: MailSearchRetrieval;
    readonly semanticSearch: MailSemanticSearch;

    /** Whether the junk folder took part. */
    readonly includedJunkMail: boolean;
}

/**
 * The largest page this surface serves, which is the deployment's bound rather than a preference.
 *
 * A screen asking for more would be refused with a `400`, so the client holds the same number and asks within it.
 */
export const longestSearchPage = 50;

/** The longest text this surface ranks against, past which a search is refused rather than shortened. */
export const longestSearchText = 512;

/**
 * How far the ranked list goes, past which the deployment answers no cursor.
 *
 * A screen holds it because it is what says a search that has run out of results has run out rather than failed:
 * somebody who has read this many without finding what they wanted narrows the filters instead of paging on.
 */
export const mostSearchResults = 200;

// The most of one page this client reads. A result carries a subject, a bounded preview, a handful of addresses, and
// its extracts, so a full page is a few hundred kilobytes at its widest; this is above that and well below the
// transport's backstop, which is written for an address nobody has trusted yet rather than for a route already
// signed in to.
const longestSearchAnswer = 512 * 1024;

// What one result may carry before the page is refused unread, checked while the results are walked rather than after.
const longestCursor = 4_096;
const longestSnippet = 4_096;
const mostSnippets = 32;

const rankings: readonly MailSearchRanking[] = ['LexicalRanking', 'SemanticRanking', 'BothRankings'];
const retrievals: readonly MailSearchRetrieval[] = ['Lexical', 'Hybrid'];
const semantics: readonly MailSemanticSearch[] = ['Inactive', 'Available', 'Degraded'];

/**
 * Reads one page of the signed-in owner's mail ranked against what they are looking for, answering an expected failure
 * as a value rather than by throwing.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param query The text to rank by, the filters that constrain, and where in the ranked list to continue from.
 * @returns The page, or why it never arrived.
 */
export async function readMailSearch(
    session: ClientSession,
    transport: MailFathomTransport,
    query: MailSearchQuery,
): Promise<ClientResult<MailSearchPage>> {
    const response = await send(transport, {
        method: 'GET',
        path: routeFor(session, mailSearchRoute) + searchQueryString(query),
        headers: headersFor(session),
        longestAnswer: longestSearchAnswer,
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(reasonForSearchStatus(response.status), response.status);
    }

    const page = parsePage(response.body, query.pageSize);

    return page === null ? failed('unreadable', response.status) : read(page);
}

/**
 * The query string one page is asked with.
 *
 * A filter the screen has nothing to put in is left out rather than sent empty, because the whole of what is sent is
 * what a cursor was issued under: a request that started spelling an absent filter would be asking for a different
 * search with a cursor taken under the one before it.
 */
export function searchQueryString(query: MailSearchQuery): string {
    const asked: string[] = [`query=${encodeURIComponent(query.text)}`, `pageSize=${String(query.pageSize)}`];

    for (const [name, named] of [
        ['account', query.account],
        ['folder', query.folder],
        ['sender', query.sender],
        ['recipient', query.recipient],
        ['receivedOnOrAfter', query.receivedOnOrAfter],
        ['receivedBefore', query.receivedBefore],
        ['cursor', query.cursor],
    ] as const) {
        if (named !== null) {
            asked.push(`${name}=${encodeURIComponent(named)}`);
        }
    }

    if (query.includeJunk) {
        asked.push('includeJunk=true');
    }

    for (const [name, wanted] of [
        ['unread', query.unread],
        ['flagged', query.flagged],
        ['hasAttachments', query.hasAttachments],
    ] as const) {
        if (wanted !== null) {
            asked.push(`${name}=${wanted ? 'true' : 'false'}`);
        }
    }

    return `?${asked.join('&')}`;
}

/**
 * The failure a status this read did not expect to succeed stands for.
 *
 * It parts from the shared mapping on exactly one status, and only because this route is the one where the client
 * composes every value it sends: the text is bounded before it is asked for, the filters are held to the shapes this
 * surface accepts, the folder is one of the roles this client publishes, and a cursor is one the deployment issued for
 * the search still being read. A `400` here is therefore this client having sent something it should not have, which
 * is a defect to report rather than something a reader can retry their way out of.
 */
function reasonForSearchStatus(status: number): ClientFailureReason {
    switch (status) {
        case 400:
            return 'unreadable';
        case 401:
            return 'unauthenticated';
        case 403:
            return 'unauthorized';
        default:
            return 'unavailable';
    }
}

// The page is held against what was asked for as well as against its own shape: a deployment answering with more
// results than the request admits is one this client refuses to render rather than one it draws.
function parsePage(body: string, asked: number): MailSearchPage | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null) {
        return null;
    }

    const rows = record['results'];
    const pageSize = record['pageSize'];
    const nextCursor = record['nextCursor'] ?? null;
    const retrievalMode = record['retrievalMode'];
    const semanticSearch = record['semanticSearch'];
    const includedJunkMail = record['includedJunkMail'];

    if (!Array.isArray(rows) || rows.length > asked) {
        return null;
    }

    if (typeof pageSize !== 'number' || !Number.isSafeInteger(pageSize) || pageSize < 1) {
        return null;
    }

    if (nextCursor !== null && (typeof nextCursor !== 'string' || nextCursor.length === 0)) {
        return null;
    }

    if (nextCursor !== null && nextCursor.length > longestCursor) {
        return null;
    }

    if (!isOneOf(retrievalMode, retrievals) || !isOneOf(semanticSearch, semantics)) {
        return null;
    }

    if (typeof includedJunkMail !== 'boolean') {
        return null;
    }

    const results: MailSearchResult[] = [];
    for (const row of rows) {
        const result = parseResult(row);

        if (result === null) {
            return null;
        }

        results.push(result);
    }

    return { results, nextCursor, pageSize, retrievalMode, semanticSearch, includedJunkMail };
}

function parseResult(value: unknown): MailSearchResult | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const entry = parseTimelineEntry(value);
    const matchedBy = record['matchedBy'];

    if (entry === null || !isOneOf(matchedBy, rankings)) {
        return null;
    }

    const snippets = parseSnippets(record['snippets']);

    return snippets === null ? null : { ...entry, snippets, matchedBy };
}

function parseSnippets(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostSnippets) {
        return null;
    }

    const extracts: string[] = [];
    for (const extract of value) {
        if (typeof extract !== 'string' || extract.length > longestSnippet) {
            return null;
        }

        extracts.push(extract);
    }

    return extracts;
}

// A value the service names one of a closed set with, which is a set this package publishes as a type: anything else
// is an answer from a deployment this client cannot draw rather than one it draws wrongly.
function isOneOf<TValue extends string>(value: unknown, offered: readonly TValue[]): value is TValue {
    return typeof value === 'string' && (offered as readonly string[]).includes(value);
}
