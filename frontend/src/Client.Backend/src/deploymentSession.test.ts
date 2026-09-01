// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readDeploymentSession } from './deploymentSession';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic QWxhZGRpbjpvcGVu' };

function answering(response: Partial<ClientResponse>): MailFathomTransport {
    return () => Promise.resolve({ status: 200, body: '', headers: {}, ...response });
}

function sessionBody(permissions: unknown, version: unknown = '0.8.0', service: unknown = 'MailFathom'): string {
    return JSON.stringify({ service, version, permissions });
}

describe('readDeploymentSession', () => {
    it('presents the credential to the session route', async () => {
        const asked: ClientRequest[] = [];

        await readDeploymentSession(session, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: sessionBody([]), headers: {} });
        });

        expect(asked).toEqual([
            {
                method: 'GET',
                path: 'https://mail.example.invalid/api/client/session',
                headers: { Accept: 'application/json', Authorization: session.authorization },
            },
        ]);
    });

    it('reads the running release and the grant the presented credential carries', async () => {
        const result = await readDeploymentSession(
            session,
            answering({ body: sessionBody(['mailfathom.mail.read', 'mailfathom.mail.ask'], '0.8.1') }),
        );

        expect(result).toEqual({
            outcome: 'read',
            value: { version: '0.8.1', permissions: ['mailfathom.mail.read', 'mailfathom.mail.ask'] },
        });
    });

    it('reads a credential granted nothing as one holding an empty grant rather than as a refusal', async () => {
        const result = await readDeploymentSession(session, answering({ body: sessionBody([]) }));

        expect(result).toEqual({ outcome: 'read', value: { version: '0.8.0', permissions: [] } });
    });

    it('keeps the names it knows out of an answer that also carries one it does not', async () => {
        const result = await readDeploymentSession(
            session,
            answering({ body: sessionBody(['mailfathom.mail.read', 'mailfathom.mail.telepathy']) }),
        );

        expect(result).toEqual({ outcome: 'read', value: { version: '0.8.0', permissions: ['mailfathom.mail.read'] } });
    });

    it('names an administrative permission nowhere, that half being one this surface never grants', async () => {
        const result = await readDeploymentSession(
            session,
            answering({ body: sessionBody(['mailfathom.admin.read']) }),
        );

        expect(result).toEqual({ outcome: 'read', value: { version: '0.8.0', permissions: [] } });
    });

    it('names a permission once however many times the answer repeated it', async () => {
        const result = await readDeploymentSession(
            session,
            answering({ body: sessionBody(['mailfathom.mail.read', 'mailfathom.mail.read']) }),
        );

        expect(result).toEqual({ outcome: 'read', value: { version: '0.8.0', permissions: ['mailfathom.mail.read'] } });
    });

    it.each([
        ['another product answering in JSON', sessionBody([], '0.8.0', 'Something else')],
        ['an answer naming no release', sessionBody([], null)],
        ['an answer naming an empty release', sessionBody([], '')],
        ['an answer carrying no grant at all', JSON.stringify({ service: 'MailFathom', version: '0.8.0' })],
        ['a grant that is not a list', sessionBody('mailfathom.mail.read')],
        ['a grant carrying something that is not a name', sessionBody(['mailfathom.mail.read', 7])],
        ['a grant longer than a grant is', sessionBody(Array.from({ length: 65 }, () => 'mailfathom.mail.read'))],
        // Refused on its size before it is parsed at all, which is the order that matters: this is the one answer the
        // client reads from an address nobody has trusted yet.
        ['a body longer than a session answer is', sessionBody([]).padEnd(4097, ' ')],
        ['a page rather than an answer', '<!doctype html><title>Sign in</title>'],
        ['an array', '[]'],
        ['nothing', ''],
    ])('refuses %s as unreadable rather than reading a grant out of it', async (_, body) => {
        const result = await readDeploymentSession(session, answering({ body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a credential the deployment has stopped accepting as one to sign in with again', async () => {
        const result = await readDeploymentSession(session, answering({ status: 401 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });

    it('reads a deployment that is failing as one to try again', async () => {
        const result = await readDeploymentSession(session, answering({ status: 503 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 503 } });
    });

    it('reports a connection that never answered as one to try again, rather than throwing at the screen', async () => {
        const result = await readDeploymentSession(session, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
