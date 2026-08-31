// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { reachDeployment, signIn } from './signIn';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic QWxhZGRpbjpvcGVu' };
const deployment = { baseAddress: session.baseAddress };

// The two challenges a MailFathom surface answers a refusal with, as one header value: the bare bearer one every
// method produces, and the password one beside it where the deployment accepts passwords.
const challengedWithBasic = 'Bearer realm="MailFathom", Basic realm="MailFathom", charset="UTF-8"';
const challengedWithoutBasic = 'Bearer realm="MailFathom"';

function answering(response: Partial<ClientResponse>): MailFathomTransport {
    return () => Promise.resolve({ status: 200, body: '', headers: {}, ...response });
}

function sessionBody(service: unknown, version: unknown): string {
    return JSON.stringify({ service, version, permissions: [] });
}

function refusedWith(challenge: string): MailFathomTransport {
    return answering({ status: 401, headers: { 'www-authenticate': challenge } });
}

describe('signIn', () => {
    it('presents the credential to the session route, which is where a deployment reports what a caller may do', async () => {
        const asked: ClientRequest[] = [];

        await signIn(session, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: sessionBody('MailFathom', '0.8.0'), headers: {} });
        });

        expect(asked).toEqual([
            {
                method: 'GET',
                path: 'https://mail.example.invalid/api/client/session',
                headers: { Accept: 'application/json', Authorization: session.authorization },
            },
        ]);
    });

    it('reads a deployment answering in full as the credential having been signed in', async () => {
        const result = await signIn(session, answering({ body: sessionBody('MailFathom', '0.8.0') }));

        expect(result).toEqual({ outcome: 'read', value: { signedIn: true } });
    });

    it('reads a refusal that still offers passwords as this credential being the thing refused', async () => {
        const result = await signIn(session, refusedWith(challengedWithBasic));

        expect(result).toEqual({ outcome: 'read', value: { signedIn: false, refusal: 'credentialRefused' } });
    });

    it('reads a refusal offering no password scheme as the deployment not taking passwords at all', async () => {
        const result = await signIn(session, refusedWith(challengedWithoutBasic));

        expect(result).toEqual({ outcome: 'read', value: { signedIn: false, refusal: 'basicNotOffered' } });
    });

    it('does not read a parameter that merely spells the scheme as an offer of it', async () => {
        const result = await signIn(session, refusedWith('Bearer realm="MailFathom", error="basic auth required"'));

        expect(result).toEqual({ outcome: 'read', value: { signedIn: false, refusal: 'basicNotOffered' } });
    });

    it('refuses a refusal challenging under somebody else, which is another product answering the port', async () => {
        const result = await signIn(session, refusedWith('Basic realm="Router"'));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 401 } });
    });

    it('refuses a refusal carrying no challenge at all', async () => {
        const result = await signIn(session, answering({ status: 401 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 401 } });
    });

    it.each([
        ['another product answering in JSON', sessionBody('Something else', '0.8.0')],
        ['a session body naming no release', sessionBody('MailFathom', null)],
        // Refused on its size before it is parsed at all, which is the order that matters: this is the one answer the
        // client reads from an address nobody has trusted yet.
        ['a body longer than a session answer is', sessionBody('MailFathom', '0.8.0').padEnd(4097, ' ')],
        ['a page rather than an answer', '<!doctype html><title>Sign in</title>'],
        ['an array', '[]'],
        ['nothing', ''],
    ])('refuses %s as a deployment', async (_, body) => {
        const result = await signIn(session, answering({ body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a grant this credential does not hold as being about what it may do rather than about who it is', async () => {
        const result = await signIn(session, answering({ status: 403 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('reads a route that is not served as something other than MailFathom answering', async () => {
        const result = await signIn(session, answering({ status: 404 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 404 } });
    });

    it('reads a deployment that is failing as one to try again rather than as a refused credential', async () => {
        const result = await signIn(session, answering({ status: 503 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 503 } });
    });

    it('reports a connection that never answered as one to try again, rather than throwing at the screen', async () => {
        const result = await signIn(session, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('makes one attempt and never a second one without the transport security the first asked for', async () => {
        const asked: string[] = [];

        await signIn(session, (request) => {
            asked.push(request.path);

            return Promise.reject(new TypeError('Failed to fetch'));
        });

        expect(asked).toEqual(['https://mail.example.invalid/api/client/session']);
    });
});

describe('reachDeployment', () => {
    it('asks the session route carrying no credential, which is the whole point of asking first', async () => {
        const asked: ClientRequest[] = [];

        await reachDeployment(deployment, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: sessionBody('MailFathom', '0.8.0'), headers: {} });
        });

        expect(asked).toEqual([
            {
                method: 'GET',
                path: 'https://mail.example.invalid/api/client/session',
                headers: { Accept: 'application/json' },
            },
        ]);
    });

    it('reads a challenge naming the password scheme as a deployment a password may be sent to', async () => {
        const result = await reachDeployment(deployment, refusedWith(challengedWithBasic));

        expect(result).toEqual({ outcome: 'read', value: { acceptsPassword: true } });
    });

    it('reads a challenge naming no password scheme as a deployment a password may not be sent to', async () => {
        const result = await reachDeployment(deployment, refusedWith(challengedWithoutBasic));

        expect(result).toEqual({ outcome: 'read', value: { acceptsPassword: false } });
    });

    it('reads a deployment that requires no credential as one a password may be sent to anyway', async () => {
        const result = await reachDeployment(deployment, answering({ body: sessionBody('MailFathom', '0.8.0') }));

        expect(result).toEqual({ outcome: 'read', value: { acceptsPassword: true } });
    });

    it('refuses a challenge from another product rather than reading it as this one', async () => {
        const result = await reachDeployment(deployment, refusedWith('Basic realm="Something Else"'));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 401 } });
    });

    it.each([
        ['a body that is not the session answer', answering({ body: '<!doctype html>' }), 'unreadable', 200],
        ['a refusal carrying no challenge at all', answering({ status: 401 }), 'unreadable', 401],
        ['an address that is not this surface', answering({ status: 404 }), 'unreadable', 404],
        ['a deployment that is failing', answering({ status: 503 }), 'unavailable', 503],
    ])('reads %s as no deployment to sign in to', async (_, transport, reason, status) => {
        const result = await reachDeployment(deployment, transport);

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reads nothing answering at all as a deployment that is unavailable', async () => {
        const result = await reachDeployment(deployment, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
