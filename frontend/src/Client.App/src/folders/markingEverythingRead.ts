// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    longestTimelinePage,
    markMailRead,
    mostMessagesPerMutation,
    readMailTimeline,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import type { MessageMarkedRead } from '../readMarking/useReadMarking';

// *Mark all as read*, which the client surface serves no route for and which is therefore composed here out of the two
// it does serve: the list, narrowed to what is unread, and the flag mutation every other reading writes.
//
// **It is bounded, and the bound is stated rather than hidden.** A folder holds as much mail as somebody has ever
// received, so a loop that ran until the list was empty would be a control that spends minutes and hundreds of
// requests on one press. It reads at most {@link mostPagesMarkedRead} pages and reports having stopped short, which is
// a sentence somebody can act on by asking again.
//
// ponytail: composed client-side out of two routes. A route on `/api/client` that marks a whole folder read is the
// upgrade, and the moment to take it is when the ceiling below is one somebody actually reaches.

/** How many pages of unread mail one press reads, which is what bounds the whole act. */
export const mostPagesMarkedRead = 20;

/** What one press came to, which is three different sentences rather than a success or a failure. */
export interface MarkedEverythingRead {
    /**
     * The messages the deployment took the marking for, which is none where nothing was unread. A batch it refused is
     * not among them, because a row drawn read on the strength of one would be a message nobody's mailbox marked.
     */
    readonly markedRead: readonly MessageMarkedRead[];

    /** Whether unread mail was left behind because the ceiling was reached, which is what says to ask again. */
    readonly leftBehind: boolean;

    /** Why it stopped, or `null` where nothing failed. */
    readonly failure: ClientFailureReason | null;
}

/**
 * Marks everything unread in a scope read.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param scope The account, and the folder inside it by alias or `null` for every folder the account has.
 * @returns What the press came to.
 */
export async function markEverythingRead(
    session: ClientSession,
    transport: MailFathomTransport,
    scope: { readonly account: string; readonly folder: string | null },
): Promise<MarkedEverythingRead> {
    const unread: MessageMarkedRead[] = [];
    let cursor: string | null = null;

    for (let page = 0; page < mostPagesMarkedRead; page += 1) {
        const answer = await readMailTimeline(session, transport, {
            account: scope.account,
            folder: scope.folder,

            // Junk is asked for, because *everything unread here* means everything: the filter exists to keep junk out
            // of a list somebody is reading, and this is not a list somebody is reading.
            includeJunk: true,
            unread: true,
            flagged: null,
            hasAttachments: null,
            receivedOnOrAfter: null,
            receivedBefore: null,
            carriesMark: null,
            markDueOnOrAfter: null,
            markDueBefore: null,
            order: 'newestFirst',
            direction: 'forward',
            pageSize: longestTimelinePage,
            cursor,
        });

        if (answer.outcome === 'failed') {
            // What was already read is still marked, because a page that answered is mail somebody asked about: the
            // failure says the rest was not reached rather than that nothing happened.
            return { ...(await submit(session, transport, unread)), failure: answer.failure.reason };
        }

        for (const entry of answer.value.emails) {
            unread.push({ storedEmailId: entry.id, account: entry.account, folder: entry.folder });
        }

        cursor = answer.value.nextCursor;

        if (cursor === null) {
            return submit(session, transport, unread);
        }
    }

    return { ...(await submit(session, transport, unread)), leftBehind: true };
}

// Writes the readings down in the batches the mutation route admits. Split rather than truncated, for the reason
// `readMarking/ReadMarking.tsx` gives about its own batches: the route refuses a longer one whole, and a message
// dropped here would be one a person was told they had read.
async function submit(
    session: ClientSession,
    transport: MailFathomTransport,
    messages: readonly MessageMarkedRead[],
): Promise<MarkedEverythingRead> {
    const batches: Promise<{
        readonly batch: readonly MessageMarkedRead[];
        readonly failure: ClientFailureReason | null;
    }>[] = [];

    for (let from = 0; from < messages.length; from += mostMessagesPerMutation) {
        const batch = messages.slice(from, from + mostMessagesPerMutation);

        batches.push(
            markMailRead(
                session,
                transport,
                batch.map(({ storedEmailId }) => storedEmailId),
            ).then((answer) => ({ batch, failure: answer.outcome === 'failed' ? answer.failure.reason : null })),
        );
    }

    const answered = await Promise.all(batches);

    return {
        markedRead: answered.flatMap(({ batch, failure }) => (failure === null ? batch : [])),
        leftBehind: false,
        failure: answered.find(({ failure }) => failure !== null)?.failure ?? null,
    };
}
