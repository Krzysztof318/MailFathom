// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    isSecureAddress,
    mostAuthorizationServers,
    originOf,
    readProtectedResource,
    readSignInMethods,
} from './signInMethods';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const deployment = { baseAddress: 'https://mail.example.invalid' };

const server = {
    issuer: 'https://id.example.invalid',
    name: 'keycloak',
    displayName: 'Keycloak',
    clientId: 'mailfathom-client',
};

function answering(response: Partial<ClientResponse>): MailFathomTransport {
    return () => Promise.resolve({ status: 200, body: '', headers: {}, ...response });
}

function publishing(document: unknown): MailFathomTransport {
    return answering({ body: JSON.stringify(document) });
}

describe('readSignInMethods', () => {
    it('asks the route beneath the client prefix, carrying no credential', async () => {
        const asked: ClientRequest[] = [];

        await readSignInMethods(deployment, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: JSON.stringify({ acceptsPassword: true }), headers: {} });
        });

        expect(asked[0]?.path).toBe('https://mail.example.invalid/api/client/sign-in-methods');
        expect(asked[0]?.headers['Authorization']).toBeUndefined();
    });

    it('reads what the deployment published, in the order it published it', async () => {
        const answer = await readSignInMethods(
            deployment,
            publishing({ acceptsPassword: false, authorizationServers: [server] }),
        );

        expect(answer).toEqual({
            outcome: 'read',
            value: { acceptsPassword: false, authorizationServers: [server] },
        });
    });

    it('reads a document with no servers in it as a deployment that publishes none', async () => {
        const answer = await readSignInMethods(deployment, publishing({ acceptsPassword: true }));

        expect(answer.outcome === 'read' && answer.value.authorizationServers).toEqual([]);
    });

    // A deployment that never published this route is an older one rather than a broken one, and the caller falls back
    // to what the session route's challenge already says about a password.
    it('reports a deployment that does not publish the route as nothing to read rather than as unreachable', async () => {
        const answer = await readSignInMethods(deployment, answering({ status: 404 }));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it.each([
        ['a document that is not an object', '"a string"'],
        ['a document that says nothing about a password', JSON.stringify({ authorizationServers: [] })],
        [
            'a server with no client identifier',
            JSON.stringify({ acceptsPassword: true, authorizationServers: [{ ...server, clientId: '' }] }),
        ],
        [
            'a server named by nothing',
            JSON.stringify({ acceptsPassword: true, authorizationServers: [{ ...server, name: 123 }] }),
        ],
        [
            'an issuer that is not an https address',
            JSON.stringify({
                acceptsPassword: true,
                authorizationServers: [{ ...server, issuer: 'http://id.example.invalid' }],
            }),
        ],
        [
            'an issuer carrying a query a discovery address could not be derived from',
            JSON.stringify({
                acceptsPassword: true,
                authorizationServers: [{ ...server, issuer: 'https://id.example.invalid/?realm=one' }],
            }),
        ],
    ])('refuses %s rather than handing a screen a provider it cannot use', async (_, body) => {
        const answer = await readSignInMethods(deployment, answering({ body }));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a list longer than a sign-in screen would ever draw', async () => {
        const answer = await readSignInMethods(
            deployment,
            publishing({
                acceptsPassword: true,
                authorizationServers: Array.from({ length: mostAuthorizationServers + 1 }, (_, at) => ({
                    ...server,
                    issuer: `https://id${String(at)}.example.invalid`,
                })),
            }),
        );

        expect(answer.outcome).toBe('failed');
    });
});

describe('readProtectedResource', () => {
    it('asks the RFC 9728 address for the client surface, which the specification places outside the prefix', async () => {
        const asked: ClientRequest[] = [];

        await readProtectedResource(deployment, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: JSON.stringify({ resource: 'x' }), headers: {} });
        });

        expect(asked[0]?.path).toBe('https://mail.example.invalid/.well-known/oauth-protected-resource/api/client');
    });

    it('reads the resource and the scopes a token has to be asked for', async () => {
        const answer = await readProtectedResource(
            deployment,
            publishing({ resource: deployment.baseAddress, scopes_supported: ['mailfathom.read'] }),
        );

        expect(answer).toEqual({
            outcome: 'read',
            value: { resource: deployment.baseAddress, scopes: ['mailfathom.read'] },
        });
    });

    // The field is optional in RFC 9728, and a resource advertising none is a client asking for none.
    it('reads a document naming no scopes as a resource that asks for none', async () => {
        const answer = await readProtectedResource(deployment, publishing({ resource: deployment.baseAddress }));

        expect(answer.outcome === 'read' && answer.value.scopes).toEqual([]);
    });

    it('reports a deployment publishing no such document as nothing to read', async () => {
        const answer = await readProtectedResource(deployment, answering({ status: 404 }));

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('missing');
    });
});

describe('isSecureAddress', () => {
    it.each([
        ['an origin', 'https://id.example.invalid', true],
        ['an origin with a port', 'https://id.example.invalid:8443', true],
        ['an issuer with a path', 'https://id.example.invalid/realms/mail', true],
        ['plain HTTP', 'http://id.example.invalid', false],
        ['a credential in the authority', 'https://user@id.example.invalid', false],
        ['a query', 'https://id.example.invalid/?realm=one', false],
        ['a fragment', 'https://id.example.invalid/#realm', false],
        ['whitespace', 'https://id.example.invalid /realms', false],
        ['nothing at all', '', false],
    ])('reads %s as %s', (_, value, accepted) => {
        expect(isSecureAddress(value)).toBe(accepted);
    });
});

describe('originOf', () => {
    it.each([
        ['https://id.example.invalid', 'https://id.example.invalid'],
        ['https://id.example.invalid:8443', 'https://id.example.invalid:8443'],
        ['https://id.example.invalid/realms/mail', 'https://id.example.invalid'],
        ['https://id.example.invalid/', 'https://id.example.invalid'],
    ])('reads the origin of %s as %s', (address, origin) => {
        expect(originOf(address)).toBe(origin);
    });
});
