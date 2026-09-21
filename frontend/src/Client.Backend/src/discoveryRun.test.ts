// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { discoveryRunRoute, readDiscoveryRunTail } from './discoveryRun';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const runId = '6f1b0a8c-2d3e-4f50-9a1b-7c8d9e0f1a2b';

function bodyOf(events: readonly unknown[], running = false): string {
    return JSON.stringify({ running, events });
}

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

describe('discoveryRunRoute', () => {
    it('names the run in the path', () => {
        expect(discoveryRunRoute(runId)).toBe(`/discovery/runs/${runId}`);
    });

    it('escapes an identifier that is not the shape the route matches', () => {
        expect(discoveryRunRoute('../runs')).toBe('/discovery/runs/..%2Fruns');
    });
});

describe('readDiscoveryRunTail', () => {
    it('reads the run from its beginning without naming a cursor', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf([]) });

        await readDiscoveryRunTail(session, transport, runId, 0);

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/discovery/runs/${runId}`);
    });

    it('carries the cursor it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf([]) });

        await readDiscoveryRunTail(session, transport, runId, 7);

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/discovery/runs/${runId}?since=7`);
    });

    it('reads whether the run is still working', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([], true) }),
            runId,
            0,
        );

        expect(answered).toEqual({ outcome: 'read', value: { running: true, events: [] } });
    });

    it('reads the revision the plan was written against off the run that started', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ event: 'started', sequence: 1, planSchemaVersion: 3 }]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: { running: false, events: [{ kind: 'started', sequence: 1, planSchemaVersion: 3 }] },
        });
    });

    it('reads a block the catalogue carries under the type it names', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ event: 'block', sequence: 4, block: { type: 'factTable' } }]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [{ kind: 'block', sequence: 4, block: { type: 'factTable', named: 'factTable' } }],
            },
        });
    });

    it('keeps the name of a block type the catalogue does not carry rather than refusing the run', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ event: 'block', sequence: 2, block: { type: 'RiskScore' } }]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [{ kind: 'block', sequence: 2, block: { type: null, named: 'RiskScore' } }],
            },
        });
    });

    it.each(['completed', 'failed', 'retrieval', 'citation'])(
        'carries the %s event as one no answer is drawn out of',
        async (event) => {
            const answered = await readDiscoveryRunTail(
                session,
                answering({ status: 200, body: bodyOf([{ event, sequence: 9 }]) }),
                runId,
                0,
            );

            expect(answered).toEqual({
                outcome: 'read',
                value: { running: false, events: [{ kind: 'other', sequence: 9 }] },
            });
        },
    );

    it('carries an event kind this contract does not name so the cursor moves past it', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ event: 'riskScored', sequence: 5, score: 3 }]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: { running: false, events: [{ kind: 'other', sequence: 5 }] },
        });
    });

    it('reads a run this user does not hold as gone rather than as something to retry', async () => {
        const answered = await readDiscoveryRunTail(session, answering({ status: 404, body: '' }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reports %i as %s', async (status, reason) => {
        const answered = await readDiscoveryRunTail(session, answering({ status, body: '' }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment it could not reach', async () => {
        const answered = await readDiscoveryRunTail(session, () => Promise.reject(new Error('down')), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        ['a body that is not JSON', 'not json'],
        ['a body that is not an object', '[]'],
        ['an answer that does not say whether the run is working', JSON.stringify({ events: [] })],
        ['events that are not a list', JSON.stringify({ running: false, events: {} })],
        ['an event that is not an object', bodyOf(['started'])],
        ['an event with no sequence', bodyOf([{ event: 'block', block: { type: 'answer' } }])],
        ['an event whose sequence is not whole', bodyOf([{ event: 'other', sequence: 1.5 }])],
        ['an event whose sequence is below the first', bodyOf([{ event: 'other', sequence: 0 }])],
        ['an event this contract cannot name', bodyOf([{ event: 7, sequence: 1 }])],
        ['a start that names no revision', bodyOf([{ event: 'started', sequence: 1 }])],
        ['a block that is not an object', bodyOf([{ event: 'block', sequence: 1, block: 'answer' }])],
        ['a block naming no type', bodyOf([{ event: 'block', sequence: 1, block: {} }])],
        ['a block whose type is empty', bodyOf([{ event: 'block', sequence: 1, block: { type: '' } }])],
    ])('refuses %s', async (_, body) => {
        const answered = await readDiscoveryRunTail(session, answering({ status: 200, body }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses more events than one run may publish', async () => {
        const events = Array.from({ length: 229 }, (_, at) => ({ event: 'other', sequence: at + 1 }));

        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf(events) }),
            runId,
            0,
        );

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a block type longer than one a screen could name', async () => {
        const body = bodyOf([{ event: 'block', sequence: 1, block: { type: 'a'.repeat(129) } }]);

        const answered = await readDiscoveryRunTail(session, answering({ status: 200, body }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
