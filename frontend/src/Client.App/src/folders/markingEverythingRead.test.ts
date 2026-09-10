// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
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
function answering(bodies: readonly (string | null)[]): {
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
                : Promise.resolve({ status: 200, headers: {}, body });
        },
    };
}

const nothingUnread = page([], null);

// Every mutation this walk makes answers the same way, which is what lets one body stand for the whole of the
// writing half: what these tests are about is which messages were named and when the walk stopped.
const recorded = JSON.stringify({ results: [{ storedEmailId: 'one', outcome: 'recorded', changes: [] }] });

describe('markEverythingRead', () => {
    it('marks nothing and reports nothing where the folder holds no unread mail', async () => {
        const { asked, transport } = answering([nothingUnread]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({ marked: 0, leftBehind: false, failure: null });
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

        expect(outcome).toEqual({ marked: 3, leftBehind: false, failure: null });

        const written = JSON.parse(asked[2]?.body ?? '{}') as { changes: readonly { storedEmailId: string }[] };

        expect(written.changes.map((change) => change.storedEmailId)).toEqual(['one', 'two', 'three']);
    });

    it('stops at its own ceiling and says so, rather than spending a press on a mailbox-sized walk', async () => {
        // A deployment with more unread mail than the walk will read: every page offers another after it.
        let read = 0;

        const outcome = await markEverythingRead(
            session,
            (request) => {
                const body = request.method === 'GET' ? page([`message-${String(read++)}`], 'next') : recorded;

                return Promise.resolve({ status: 200, headers: {}, body });
            },
            { account: 'work', folder: 'INBOX' },
        );

        expect(outcome.leftBehind).toBe(true);
        expect(outcome.marked).toBe(mostPagesMarkedRead);
    });

    it('marks what it had already found when a later page does not answer, and says why it stopped', async () => {
        const { transport } = answering([page(['one'], 'next'), null, recorded]);
        const outcome = await markEverythingRead(session, transport, { account: 'work', folder: 'INBOX' });

        expect(outcome).toEqual({ marked: 1, leftBehind: false, failure: 'unavailable' });
    });
});
