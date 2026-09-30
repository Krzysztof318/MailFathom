// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { mostMessagesPerMutation } from '@mailfathom/client-backend';
import { markEverythingRead, mostPagesMarkedRead } from './markingEverythingRead';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

function message(id: string): Readonly<Record<string, unknown>> {
    return {
        id,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: 'Something',
        receivedAt: '2026-09-01T09:00:00+00:00',
        sentAt: null,
        senderAddress: 'someone@example.invalid',
        senderDisplayName: null,
        toAddresses: [],
        unread: true,
        flagged: false,
        answered: false,
        hasAttachments: false,
        attachmentCount: 0,
        sizeOctets: 100,
        preview: null,
        enrichment: null,
        threadMessageCount: null,
    };
}

function page(ids: readonly string[], nextCursor: string | null): string {
    return JSON.stringify({
        emails: ids.map(message),
        nextCursor,
        previousCursor: null,
        pageSize: 100,
    });
}

/**
 * A deployment answering each request in turn, and the requests it was asked, so the walk itself can be read.
 *
 * A `null` among the bodies is a deployment that could not be reached at all, which a transport reports by throwing —
 * `send` is what turns that into no answer.
 */
function answering(bodies: readonly (string | ((request: ClientRequest) => string) | null)[]): {
    asked: ClientRequest[];
    transport: MailFathomTransport;
} {
    const asked: ClientRequest[] = [];
    let at = 0;

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const body = bodies[Math.min(at, bodies.length - 1)];

            at += 1;

            return body === undefined || body === null
                ? Promise.reject(new Error('no route'))
                : Promise.resolve({ status: 200, headers: {}, body: typeof body === 'string' ? body : body(request) });
        },
    };
}

const nothingUnread = page([], null);

function inTheInbox(ids: readonly string[]) {
    return ids.map((storedEmailId) => ({ storedEmailId, account: 'work', folder: 'INBOX' }));
}

// A mutation answered per message, as the route answers one: each message the request named, with what became of it.
function answeredWith(outcomeOf: (storedEmailId: string) => string): (request: ClientRequest) => string {
    return (request) => {
        const written = JSON.parse(request.body ?? '{}') as { changes: readonly { storedEmailId: string }[] };

        return JSON.stringify({
            results: written.changes.map(({ storedEmailId }) => ({
                storedEmailId,
                outcome: outcomeOf(storedEmailId),
                changes: [],
            })),
        });
    };
}

const recorded = answeredWith(() => 'recorded');

function numbered(count: number, from = 0): string[] {
    return Array.from({ length: count }, (_, at) => `message-${String(from + at)}`);
}

describe('markEverythingRead', () => {
    it('marks nothing and reports nothing where the folder holds no unread mail', async () => {
        const { asked, transport } = answering([nothingUnread]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({ markedRead: [], leftBehind: false, failure: null });
        expect(asked).toHaveLength(1);
    });

    it('narrows the list to the unread mail of that folder, junk included, because *everything* means everything', async () => {
        const { asked, transport } = answering([nothingUnread]);

        await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(asked[0]?.path).toContain('unread=true');
        expect(asked[0]?.path).toContain('includeJunk=true');
        expect(asked[0]?.path).toContain('folder=INBOX');
    });

    it('asks about the whole mailbox where no folder is named', async () => {
        const { asked, transport } = answering([nothingUnread]);

        await markEverythingRead(session, transport, { account: 'work', folder: null });

        expect(asked[0]?.path).not.toContain('folder=');
    });

    it('follows the pages the deployment offers and marks everything it found in one batch', async () => {
        const { asked, transport } = answering([page(['one', 'two'], 'next'), page(['three'], null), recorded]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({
            markedRead: inTheInbox(['one', 'two', 'three']),
            leftBehind: false,
            failure: null,
        });

        const written = JSON.parse(asked[2]?.body ?? '{}') as { changes: readonly { storedEmailId: string }[] };

        expect(written.changes.map((change) => change.storedEmailId)).toEqual(['one', 'two', 'three']);
    });

    it('stops at its own ceiling and says so, rather than spending a press on a mailbox-sized walk', async () => {
        // A deployment with more unread mail than the walk will read: every page offers another after it.
        let read = 0;

        const outcome = await markEverythingRead(
            session,
            (request) => {
                const body = request.method === 'GET' ? page([`message-${String(read++)}`], 'next') : recorded(request);

                return Promise.resolve({ status: 200, headers: {}, body });
            },
            { account: 'work', folder: 'INBOX' },
        );

        expect(outcome.leftBehind).toBe(true);
        expect(outcome.markedRead).toHaveLength(mostPagesMarkedRead);
    });

    it('marks what it had already found when a later page does not answer, and says why it stopped', async () => {
        const { transport } = answering([page(['one'], 'next'), null, recorded]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({ markedRead: inTheInbox(['one']), leftBehind: false, failure: 'unavailable' });
    });

    it('counts nothing as marked read where the deployment could not be reached to mark it', async () => {
        const { transport } = answering([page(['one'], null), null]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({ markedRead: [], leftBehind: false, failure: 'unavailable' });
    });

    it('draws read only the batches the deployment took where one of several could not be written', async () => {
        const ids = numbered(mostMessagesPerMutation + 1);
        const { transport } = answering([
            page(ids.slice(0, 100), 'next'),
            page(ids.slice(100, 200), 'next'),
            page(ids.slice(200), null),
            recorded,
            null,
        ]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({
            markedRead: inTheInbox(ids.slice(0, mostMessagesPerMutation)),
            leftBehind: false,
            failure: 'unavailable',
        });
    });

    it('leaves out a message the deployment refused while the rest of its batch was taken', async () => {
        const { transport } = answering([
            page(['one', 'moved', 'three'], null),
            answeredWith((storedEmailId) => (storedEmailId === 'moved' ? 'message-not-found' : 'applied')),
        ]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({ markedRead: inTheInbox(['one', 'three']), leftBehind: false, failure: null });
    });
});
