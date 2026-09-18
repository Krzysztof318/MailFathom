// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { contactRoute, contactsRoute } from './contacts';
import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What the mail this caller may already read holds about one contact: the conversations their addresses appear in, and
// the documents they sent. It is a route of its own rather than part of the record, because the two cost different
// things — a record is a row, and this is a correlation computed over the mail index on every read.
//
// Nothing here is stored, so a contact never carries a copy of a correspondence that could fall behind the mailbox.
// Both lists are bounded and neither is paged: what an opened contact draws is a card rather than a mailbox.
//
// Every identity in the answer is one another route is asked with directly — the conversation, the message, and the
// attachment — so nothing in either list has to be resolved before it can be opened.

/** The route one contact's correlation is served at, relative to the client prefix. */
export function contactCorrespondenceRoute(contactId: string): string {
    return `${contactRoute(contactId)}/correspondence`;
}

/** One conversation a contact takes part in, read off the most recent message in it naming them. */
export interface CorrespondingThread {
    readonly threadId: string;

    /** The most recent message of it naming this person, which the message route is asked with. */
    readonly latestMessageId: string;

    /** What that message was about, or `null` where it carried none. */
    readonly subject: string | null;

    readonly lastCorrespondedAt: string;
}

/** One document a contact sent, on a message written from one of their own addresses. */
export interface CorrespondingDocument {
    readonly messageId: string;

    /** Where the file sits in that message's walk, which is the other half of what the attachment route is asked with. */
    readonly position: number;

    /** The name the sender wrote, or `null` where the part carried none. */
    readonly fileName: string | null;

    /** The type the sender declared, which draws a symbol and is never trusted as the file's content. */
    readonly mediaType: string;

    readonly receivedAt: string;
}

/** What one contact's correspondence holds, as of the instant it was read. */
export interface ContactCorrespondence {
    readonly contactId: string;
    readonly threads: readonly CorrespondingThread[];
    readonly documents: readonly CorrespondingDocument[];
}

/**
 * The most entries either list may carry, which is the deployment's own bound rather than a preference.
 *
 * The route is not paged, so this is the whole of what it can answer with and an answer longer than it is one this
 * package refuses rather than renders.
 */
export const mostCorrespondenceEntries = 10;

// Ten conversations and ten documents of short fields each. Generous against that arithmetic and far under the
// transport's own backstop.
const longestCorrespondenceAnswer = 64 * 1024;

/**
 * Reads what the mail this caller may see already holds about one contact.
 *
 * A `404` is `missing` rather than `unavailable`, for the reason `readContact` gives: this route names one person, and
 * a contact no book in this caller's scope holds is something to let go of rather than to retry.
 */
export function readContactCorrespondence(
    session: ClientSession,
    transport: MailFathomTransport,
    contactId: string,
): Promise<ClientResult<ContactCorrespondence>> {
    return spanned(`GET ${contactsRoute}/{contactId}/correspondence`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, contactCorrespondenceRoute(contactId)),
            headers: headersFor(session),
            longestAnswer: longestCorrespondenceAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const correspondence = parseCorrespondence(response.body);

        return correspondence === null ? failed('unreadable', response.status) : read(correspondence);
    });
}

function parseCorrespondence(body: string): ContactCorrespondence | null {
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

    const contactId = record['contactId'];
    const threadEntries = record['threads'];
    const documentEntries = record['documents'];

    if (typeof contactId !== 'string' || !Array.isArray(threadEntries) || !Array.isArray(documentEntries)) {
        return null;
    }

    if (threadEntries.length > mostCorrespondenceEntries || documentEntries.length > mostCorrespondenceEntries) {
        return null;
    }

    const threads: CorrespondingThread[] = [];
    for (const entry of threadEntries) {
        const thread = parseThread(entry);
        if (thread === null) {
            return null;
        }

        threads.push(thread);
    }

    const documents: CorrespondingDocument[] = [];
    for (const entry of documentEntries) {
        const document = parseDocument(entry);
        if (document === null) {
            return null;
        }

        documents.push(document);
    }

    return { contactId, threads, documents };
}

function parseThread(value: unknown): CorrespondingThread | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const threadId = record['threadId'];
    const latestMessageId = record['latestMessageId'];
    const subject = record['subject'] ?? null;
    const lastCorrespondedAt = record['lastCorrespondedAt'];

    if (typeof threadId !== 'string' || typeof latestMessageId !== 'string') {
        return null;
    }

    if (typeof lastCorrespondedAt !== 'string' || (subject !== null && typeof subject !== 'string')) {
        return null;
    }

    return { threadId, latestMessageId, subject, lastCorrespondedAt };
}

function parseDocument(value: unknown): CorrespondingDocument | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const messageId = record['messageId'];
    const position = record['position'];
    const fileName = record['fileName'] ?? null;
    const mediaType = record['mediaType'];
    const receivedAt = record['receivedAt'];

    if (typeof messageId !== 'string' || typeof position !== 'number' || !Number.isInteger(position)) {
        return null;
    }

    if (typeof mediaType !== 'string' || typeof receivedAt !== 'string') {
        return null;
    }

    if (fileName !== null && typeof fileName !== 'string') {
        return null;
    }

    return { messageId, position, fileName, mediaType, receivedAt };
}
