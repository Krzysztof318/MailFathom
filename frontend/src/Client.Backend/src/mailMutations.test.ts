// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    changeMailFlags,
    markMailRead,
    mostMessagesPerMutation,
    mostRecordsPerRead,
    moveMail,
    readMailMutationRecords,
} from './mailMutations';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const storedEmailId = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';
const recordId = '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91';

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
                { storedEmailId, outcome: 'recorded', changes: [] },
                { storedEmailId: 'gone', outcome: 'message-not-found', changes: [] },
            ],
        });
    });

    it('answers the records a written-down change became, so its caller can follow each of them', async () => {
        const answer = await markMailRead(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    results: [
                        {
                            storedEmailId,
                            outcome: 'recorded',
                            changes: [{ recordId, mutation: 'set-seen', state: 'pending' }],
                        },
                    ],
                }),
            }),
            [storedEmailId],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [{ storedEmailId, outcome: 'recorded', changes: [{ recordId, state: 'pending' }] }],
        });
    });

    it('refuses a record standing somewhere this client does not know, rather than following it blind', async () => {
        const answer = await markMailRead(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    results: [{ storedEmailId, outcome: 'recorded', changes: [{ recordId, state: 'halfway' }] }],
                }),
            }),
            [storedEmailId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
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

    // The records one message became are what the queue follows it by, so a list that is not one is refused rather
    // than read as a message that wrote nothing down — which is the shape a change nobody follows would arrive in.
    it('refuses a message whose records are not a list', async () => {
        const answer = await markMailRead(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ results: [{ storedEmailId, outcome: 'recorded', changes: 'one' }] }),
            }),
            [storedEmailId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a message naming more records than one change can produce', async () => {
        const answer = await markMailRead(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    results: [
                        {
                            storedEmailId,
                            outcome: 'recorded',
                            // One past the eight the module bounds a single message's records at, which is itself
                            // well above the three a flag route can write — the bound is not a caller's to name.
                            changes: Array.from({ length: 9 }, (_, at) => ({
                                recordId: `record-${String(at)}`,
                                state: 'pending',
                            })),
                        },
                    ],
                }),
            }),
            [storedEmailId],
        );

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
                { storedEmailId, outcome: 'recorded', changes: [] },
                { storedEmailId: 'second', outcome: 'already-in-destination', changes: [] },
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

describe('readMailMutationRecords', () => {
    it('names each record it asks about on the client surface’s mutation route', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ changes: [] }) });

        await readMailMutationRecords(session, transport, [recordId, 'a b']);

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe(
            `https://mail.example.invalid/api/client/mutations?record=${recordId}&record=a%20b`,
        );
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('answers where each change stands', async () => {
        const answer = await readMailMutationRecords(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    changes: [
                        {
                            recordId,
                            storedEmailId,
                            mutation: 'set-seen',
                            state: 'converging',
                            outcomeUnknown: false,
                            attemptCount: 2,
                        },
                    ],
                }),
            }),
            [recordId],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [{ recordId, storedEmailId, state: 'converging', outcomeUnknown: false }],
        });
    });

    it('reads a record whose command went out and was never answered as one whose outcome is unknown', async () => {
        const answer = await readMailMutationRecords(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    changes: [{ recordId, storedEmailId, state: 'completed', outcomeUnknown: true }],
                }),
            }),
            [recordId],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [{ recordId, storedEmailId, state: 'completed', outcomeUnknown: true }],
        });
    });

    // A record belonging to somebody else, or one in a folder this caller may no longer read, is absent rather than
    // refused — so an answer shorter than the read is the ordinary case and says nothing went wrong.
    it('reads an answer naming none of the records asked about as an empty answer', async () => {
        const answer = await readMailMutationRecords(
            session,
            answering({ status: 200, body: JSON.stringify({ changes: [] }) }),
            [recordId],
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: [] });
    });

    it('names no more records than one read may carry', async () => {
        const asked = Array.from({ length: mostRecordsPerRead + 5 }, (_, at) => `record-${String(at)}`);
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ changes: [] }) });

        await readMailMutationRecords(session, transport, asked);

        expect(requests[0]?.path.split('record=')).toHaveLength(mostRecordsPerRead + 1);
    });

    it('refuses an answer naming more records than the read could have', async () => {
        const answer = await readMailMutationRecords(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    changes: Array.from({ length: mostRecordsPerRead + 1 }, () => ({
                        recordId,
                        storedEmailId,
                        state: 'pending',
                        outcomeUnknown: false,
                    })),
                }),
            }),
            [recordId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a record naming no message, rather than following a change against nothing', async () => {
        const answer = await readMailMutationRecords(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ changes: [{ recordId, state: 'pending', outcomeUnknown: false }] }),
            }),
            [recordId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    // The one field on this answer a person acts on rather than waits through, so a value that is not the boolean it
    // is declared as is refused: read loosely, it would say a mailbox is settled where the deployment said nothing.
    it('refuses a record whose unknown-outcome field is not a boolean', async () => {
        const answer = await readMailMutationRecords(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    changes: [{ recordId, storedEmailId, state: 'completed', outcomeUnknown: 'yes' }],
                }),
            }),
            [recordId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('says the deployment could not be reached where nothing answered', async () => {
        const answer = await readMailMutationRecords(
            session,
            () => Promise.reject(new Error('the connection was refused')),
            [recordId],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('says the credential must sign in again where the deployment refused it', async () => {
        const answer = await readMailMutationRecords(session, answering({ status: 401, body: '' }), [recordId]);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });
});
