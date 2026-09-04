// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { changeMailFlags, markMailRead, mostMessagesPerMutation, moveMail } from './mailMutations';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const storedEmailId = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

function recorded(...storedEmailIds: readonly string[]): Answer {
    return {
        status: 200,
        body: JSON.stringify({
            results: storedEmailIds.map((id) => ({ storedEmailId: id, outcome: 'recorded' })),
        }),
    };
}

describe('markMailRead', () => {
    it('states the seen flag on the client surface’s flag route for each message named', async () => {
        const { transport, requests } = recording(recorded(storedEmailId));

        await markMailRead(session, transport, [storedEmailId]);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/mutations/flags');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
        expect(requests[0]?.headers['Content-Type']).toBe('application/json');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            changes: [{ storedEmailId, flags: { seen: true } }],
        });
    });

    it('answers what became of each message', async () => {
        const answer = await markMailRead(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    results: [
                        { storedEmailId, outcome: 'recorded' },
                        { storedEmailId: 'gone', outcome: 'message-not-found' },
                    ],
                }),
            }),
            [storedEmailId, 'gone'],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [
                { storedEmailId, outcome: 'recorded' },
                { storedEmailId: 'gone', outcome: 'message-not-found' },
            ],
        });
    });

    // The deployment refuses a batch past its bound whole rather than partly, so a caller that sent one would have
    // marked nothing at all — which is why the size is the contract's rather than something discovered from a refusal.
    it('names no more messages than one submission may carry', async () => {
        const asked = Array.from({ length: mostMessagesPerMutation + 5 }, (_, at) => `message-${String(at)}`);
        const { transport, requests } = recording(recorded());

        await markMailRead(session, transport, asked);

        const sent = JSON.parse(requests[0]?.body ?? '') as { changes: readonly { storedEmailId: string }[] };

        expect(sent.changes).toHaveLength(mostMessagesPerMutation);
        expect(sent.changes[mostMessagesPerMutation - 1]?.storedEmailId).toBe(
            `message-${String(mostMessagesPerMutation - 1)}`,
        );
    });

    it('says the deployment could not be reached where nothing answered', async () => {
        const answer = await markMailRead(session, () => Promise.reject(new Error('the connection was refused')), [
            storedEmailId,
        ]);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('says the credential may not do this where the deployment refused it', async () => {
        const answer = await markMailRead(session, answering({ status: 403, body: '' }), [storedEmailId]);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('refuses an outcome this client does not know, rather than drawing a conclusion from it', async () => {
        const answer = await markMailRead(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ results: [{ storedEmailId, outcome: 'partly-recorded' }] }),
            }),
            [storedEmailId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer naming more messages than the batch could have', async () => {
        const answer = await markMailRead(
            session,
            answering(recorded(...Array.from({ length: mostMessagesPerMutation + 1 }, () => storedEmailId))),
            [storedEmailId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer that is not a batch of results at all', async () => {
        const answer = await markMailRead(session, answering({ status: 200, body: 'not json' }), [storedEmailId]);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('changeMailFlags', () => {
    it('states only the flags a change named, so an unstated one is left where it stands', async () => {
        const { transport, requests } = recording(recorded(storedEmailId));

        await changeMailFlags(session, transport, [{ storedEmailId, flagged: true }]);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/mutations/flags');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            changes: [{ storedEmailId, flags: { flagged: true } }],
        });
    });

    it('carries a whole batch of changes in one submission', async () => {
        const { transport, requests } = recording(recorded(storedEmailId, 'second'));

        await changeMailFlags(session, transport, [
            { storedEmailId, seen: false },
            { storedEmailId: 'second', seen: false },
        ]);

        expect(requests).toHaveLength(1);
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            changes: [
                { storedEmailId, flags: { seen: false } },
                { storedEmailId: 'second', flags: { seen: false } },
            ],
        });
    });

    it('names no more messages than one submission may carry', async () => {
        const asked = Array.from({ length: mostMessagesPerMutation + 5 }, (_, at) => ({
            storedEmailId: `message-${String(at)}`,
            flagged: true,
        }));
        const { transport, requests } = recording(recorded());

        await changeMailFlags(session, transport, asked);

        const sent = JSON.parse(requests[0]?.body ?? '') as { changes: readonly unknown[] };

        expect(sent.changes).toHaveLength(mostMessagesPerMutation);
    });
});

describe('moveMail', () => {
    it('names the destination folder on the client surface’s move route', async () => {
        const { transport, requests } = recording(recorded(storedEmailId));

        await moveMail(session, transport, [{ storedEmailId, destinationFolder: 'work-archive' }]);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/mutations/moves');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            moves: [{ storedEmailId, destinationFolder: 'work-archive' }],
        });
    });

    it('answers what became of each message, including one already filed there', async () => {
        const answer = await moveMail(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    results: [
                        { storedEmailId, outcome: 'recorded', destinationFolder: 'work-archive' },
                        { storedEmailId: 'second', outcome: 'already-in-destination', destinationFolder: null },
                    ],
                }),
            }),
            [{ storedEmailId, destinationFolder: 'work-archive' }],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [
                { storedEmailId, outcome: 'recorded' },
                { storedEmailId: 'second', outcome: 'already-in-destination' },
            ],
        });
    });

    it('names no more messages than one submission may carry', async () => {
        const asked = Array.from({ length: mostMessagesPerMutation + 5 }, (_, at) => ({
            storedEmailId: `message-${String(at)}`,
            destinationFolder: 'work-archive',
        }));
        const { transport, requests } = recording(recorded());

        await moveMail(session, transport, asked);

        const sent = JSON.parse(requests[0]?.body ?? '') as { moves: readonly unknown[] };

        expect(sent.moves).toHaveLength(mostMessagesPerMutation);
    });

    it('says the credential may not move mail where the deployment refused it', async () => {
        const answer = await moveMail(session, answering({ status: 403, body: '' }), [
            { storedEmailId, destinationFolder: 'work-archive' },
        ]);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });
});
