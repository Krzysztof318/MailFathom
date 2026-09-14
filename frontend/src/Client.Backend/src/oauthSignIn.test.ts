// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    authorizationRequestAddress,
    discoveryAddresses,
    readAuthorizationServer,
    redeemAuthorizationCode,
    refreshAccessToken,
    revokeRefreshToken,
    type AuthorizationServerMetadata,
} from './oauthSignIn';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const issuer = 'https://id.example.invalid';
const resource = 'https://mail.example.invalid';

const metadata: AuthorizationServerMetadata = {
    issuer,
    authorizationEndpoint: `${issuer}/authorize`,
    tokenEndpoint: `${issuer}/token`,
    revocationEndpoint: `${issuer}/revoke`,
    opensIdentity: false,
};

function answering(response: Partial<ClientResponse>): MailFathomTransport {
    return () => Promise.resolve({ status: 200, body: '', headers: {}, ...response });
}

/** A server answering its discovery document at whichever of the candidate addresses is asked for. */
function discovering(document: unknown, at = `${issuer}/.well-known/oauth-authorization-server`): MailFathomTransport {
    return (request) =>
        Promise.resolve(
            request.path === at
                ? { status: 200, body: JSON.stringify(document), headers: {} }
                : { status: 404, body: '', headers: {} },
        );
}

/**
 * What one parameter of a form-encoded body or a query says, or `null` where it states none.
 *
 * Read by hand rather than with the platform's own parser, for the same reason the package composes these strings by
 * hand: `URL` and `URLSearchParams` are DOM, this package declares ES2023 alone, and a test that reached for one would
 * be testing against a library the code under it does not have.
 */
function stated(stating: string | undefined, parameter: string): string | null {
    const query = stating === undefined ? '' : stating.slice(stating.indexOf('?') + 1);

    for (const pair of query.split('&')) {
        const separator = pair.indexOf('=');

        if (pair.slice(0, separator) === parameter) {
            return decodeURIComponent(pair.slice(separator + 1));
        }
    }

    return null;
}

describe('discoveryAddresses', () => {
    it('asks both specifications at an issuer with no path', () => {
        expect(discoveryAddresses(issuer)).toEqual([
            `${issuer}/.well-known/oauth-authorization-server`,
            `${issuer}/.well-known/openid-configuration`,
        ]);
    });

    // An issuer with a path has a third candidate, which is the one form OpenID Connect Discovery states and the two
    // RFC 8414 states are the other two.
    it('asks all three at an issuer with a path, most specific specification first', () => {
        expect(discoveryAddresses(`${issuer}/realms/mail`)).toEqual([
            `${issuer}/.well-known/oauth-authorization-server/realms/mail`,
            `${issuer}/.well-known/openid-configuration/realms/mail`,
            `${issuer}/realms/mail/.well-known/openid-configuration`,
        ]);
    });
});

describe('readAuthorizationServer', () => {
    it('reads the endpoints out of the document the issuer published about itself', async () => {
        const answer = await readAuthorizationServer(
            issuer,
            discovering({
                issuer,
                authorization_endpoint: `${issuer}/authorize`,
                token_endpoint: `${issuer}/token`,
                revocation_endpoint: `${issuer}/revoke`,
            }),
        );

        expect(answer).toEqual({ outcome: 'read', value: metadata });
    });

    it('falls through to the next candidate where the first answers nothing', async () => {
        const asked: string[] = [];
        const answer = await readAuthorizationServer(issuer, (request) => {
            asked.push(request.path);

            return request.path.endsWith('/openid-configuration')
                ? Promise.resolve({
                      status: 200,
                      body: JSON.stringify({
                          issuer,
                          authorization_endpoint: `${issuer}/authorize`,
                          token_endpoint: `${issuer}/token`,
                          userinfo_endpoint: `${issuer}/userinfo`,
                      }),
                      headers: {},
                  })
                : Promise.resolve({ status: 404, body: '', headers: {} });
        });

        expect(asked).toEqual(discoveryAddresses(issuer));
        expect(answer.outcome === 'read' && answer.value.opensIdentity).toBe(true);
        expect(answer.outcome === 'read' && answer.value.revocationEndpoint).toBeNull();
    });

    // A discovery document is a server's statement about itself, and one naming somewhere else is either misconfigured
    // or is a server being used to point this client at an address the deployment never published.
    it('refuses a document whose issuer is not the one that was asked', async () => {
        const answer = await readAuthorizationServer(
            issuer,
            discovering({
                issuer: 'https://elsewhere.example.invalid',
                authorization_endpoint: `${issuer}/authorize`,
                token_endpoint: `${issuer}/token`,
            }),
        );

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('unreadable');
    });

    // RFC 6749 §3.1 permits an authorization endpoint to carry a query of its own, and the composition below is
    // written for exactly that case — so a document publishing one has to be read rather than refused, or that
    // provider's control can never be followed at all.
    it('reads an endpoint that carries a query of its own, which the specification permits', async () => {
        const answer = await readAuthorizationServer(
            issuer,
            discovering({
                issuer,
                authorization_endpoint: `${issuer}/authorize?realm=mail`,
                token_endpoint: `${issuer}/token`,
            }),
        );

        expect(answer.outcome === 'read' && answer.value.authorizationEndpoint).toBe(`${issuer}/authorize?realm=mail`);
    });

    // The query is permitted and the origin check is not relaxed by it: an address whose authority ends at a `?` is
    // still read for the authority rather than for everything up to the first slash.
    it('refuses an endpoint carrying a query whose own origin is somewhere else', async () => {
        const answer = await readAuthorizationServer(
            issuer,
            discovering({
                issuer,
                authorization_endpoint: 'https://elsewhere.example.invalid?realm=mail',
                token_endpoint: `${issuer}/token`,
            }),
        );

        expect(answer.outcome).toBe('failed');
    });

    it('refuses an endpoint that sits on another origin than the issuer', async () => {
        const answer = await readAuthorizationServer(
            issuer,
            discovering({
                issuer,
                authorization_endpoint: 'https://elsewhere.example.invalid/authorize',
                token_endpoint: `${issuer}/token`,
            }),
        );

        expect(answer.outcome).toBe('failed');
    });

    it('refuses an issuer that is not an https address rather than calling it', async () => {
        const asked: ClientRequest[] = [];
        const answer = await readAuthorizationServer('http://id.example.invalid', (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: '', headers: {} });
        });

        expect(asked).toEqual([]);
        expect(answer.outcome).toBe('failed');
    });

    it('separates a server nothing reached from one that answered and published nothing usable', async () => {
        const unreachable = await readAuthorizationServer(issuer, () => Promise.reject(new TypeError('no route')));
        const unusable = await readAuthorizationServer(issuer, answering({ body: '{}' }));

        expect(unreachable.outcome === 'failed' && unreachable.failure.reason).toBe('unavailable');
        expect(unusable.outcome === 'failed' && unusable.failure.reason).toBe('unreadable');
    });
});

describe('authorizationRequestAddress', () => {
    it('states the code flow, the challenge, and what the token is for, and never the verifier', () => {
        const composed = authorizationRequestAddress({
            metadata,
            clientId: 'mailfathom-client',
            redirectUri: 'http://127.0.0.1:8766/',
            resource,
            scopes: ['mailfathom.read', 'mailfathom.write'],
            state: 'a-state',
            codeChallenge: 'a-challenge',
            nonce: null,
        });

        expect(composed.startsWith(`${issuer}/authorize?`)).toBe(true);
        expect(stated(composed, 'response_type')).toBe('code');
        expect(stated(composed, 'client_id')).toBe('mailfathom-client');
        expect(stated(composed, 'redirect_uri')).toBe('http://127.0.0.1:8766/');
        expect(stated(composed, 'state')).toBe('a-state');
        expect(stated(composed, 'code_challenge')).toBe('a-challenge');
        expect(stated(composed, 'code_challenge_method')).toBe('S256');
        expect(stated(composed, 'resource')).toBe(resource);
        expect(stated(composed, 'scope')).toBe('mailfathom.read mailfathom.write');
        expect(stated(composed, 'nonce')).toBeNull();
        expect(stated(composed, 'code_verifier')).toBeNull();
    });

    it('carries a nonce only where the server is an OpenID provider', () => {
        const composed = authorizationRequestAddress({
            metadata: { ...metadata, opensIdentity: true },
            clientId: 'mailfathom-client',
            redirectUri: 'http://127.0.0.1:8766/',
            resource,
            scopes: [],
            state: 'a-state',
            codeChallenge: 'a-challenge',
            nonce: 'a-nonce',
        });

        expect(stated(composed, 'nonce')).toBe('a-nonce');
        expect(stated(composed, 'scope')).toBeNull();
    });

    // An authorization endpoint is permitted to carry a query of its own, and a server that publishes one means it.
    it('keeps a query the endpoint already carried rather than replacing it', () => {
        const composed = authorizationRequestAddress({
            metadata: { ...metadata, authorizationEndpoint: `${issuer}/authorize?realm=mail` },
            clientId: 'mailfathom-client',
            redirectUri: 'http://127.0.0.1:8766/',
            resource,
            scopes: [],
            state: 'a-state',
            codeChallenge: 'a-challenge',
            nonce: null,
        });

        expect(stated(composed, 'realm')).toBe('mail');
        expect(stated(composed, 'response_type')).toBe('code');
    });
});

describe('redeemAuthorizationCode', () => {
    const redemption = {
        metadata,
        clientId: 'mailfathom-client',
        redirectUri: 'http://127.0.0.1:8766/',
        resource,
        code: 'a-code',
        codeVerifier: 'a-verifier',
    };

    it('presents the code and the verifier to the token endpoint, and no client secret', async () => {
        const asked: ClientRequest[] = [];

        await redeemAuthorizationCode(redemption, (request) => {
            asked.push(request);

            return Promise.resolve({
                status: 200,
                body: JSON.stringify({ access_token: 'a-token', token_type: 'Bearer' }),
                headers: {},
            });
        });

        expect(asked[0]?.method).toBe('POST');
        expect(asked[0]?.path).toBe(`${issuer}/token`);
        expect(asked[0]?.headers['Content-Type']).toBe('application/x-www-form-urlencoded');

        const body = asked[0]?.body;

        expect(stated(body, 'grant_type')).toBe('authorization_code');
        expect(stated(body, 'code')).toBe('a-code');
        expect(stated(body, 'code_verifier')).toBe('a-verifier');
        expect(stated(body, 'resource')).toBe(resource);
        expect(stated(body, 'client_secret')).toBeNull();
    });

    it('reads the token, its life, and the refresh token beside it', async () => {
        const answer = await redeemAuthorizationCode(
            redemption,
            answering({
                body: JSON.stringify({
                    access_token: 'a-token',
                    token_type: 'Bearer',
                    expires_in: 3600,
                    refresh_token: 'a-refresh-token',
                }),
            }),
        );

        expect(answer).toEqual({
            outcome: 'issued',
            token: { accessToken: 'a-token', expiresInSeconds: 3600, refreshToken: 'a-refresh-token' },
        });
    });

    // What the caller computes from this is an instant, and an instant past what a `Date` can hold throws where it is
    // composed rather than being refused. A life no server would mean is read as a server having said nothing, which
    // is the assumed hour.
    it.each([
        ['a life longer than a year', 60 * 60 * 24 * 400],
        ['a life no arithmetic can hold', Number.MAX_SAFE_INTEGER],
        ['something that is not a number at all', Number.NaN],
    ])('reads %s as the server having stated no life', async (_, expiresIn) => {
        const answer = await redeemAuthorizationCode(
            redemption,
            answering({
                body: JSON.stringify({ access_token: 'a-token', token_type: 'Bearer', expires_in: expiresIn }),
            }),
        );

        expect(answer.outcome === 'issued' && answer.token.expiresInSeconds).toBeNull();
    });

    // The bound is what the client can store a grant under and what a deployment would accept in a request header, so
    // an answer past it is a token nothing downstream could present anyway.
    it('refuses a token past what any authorization server issues', async () => {
        const answer = await redeemAuthorizationCode(
            redemption,
            answering({
                body: JSON.stringify({ access_token: 'a'.repeat(9000), token_type: 'Bearer' }),
            }),
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    // RFC 6749 answers a refused grant with `400` and a client it could not authenticate with `401`. Both are the same
    // sentence to somebody on the screen.
    it.each([400, 401])('reads a %s as the server refusing rather than as a failure to reach it', async (status) => {
        const answer = await redeemAuthorizationCode(
            redemption,
            answering({ status, body: '{"error":"invalid_grant"}' }),
        );

        expect(answer).toEqual({ outcome: 'refused' });
    });

    it('reads a server that answered nothing as unavailable rather than as a refusal', async () => {
        const answer = await redeemAuthorizationCode(redemption, () => Promise.reject(new TypeError('no route')));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable' } });
    });

    it('refuses an answer carrying no bearer token rather than presenting whatever it said', async () => {
        const answer = await redeemAuthorizationCode(
            redemption,
            answering({ body: JSON.stringify({ access_token: 'a-token', token_type: 'mac' }) }),
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });
});

describe('refreshAccessToken', () => {
    const renewal = { metadata, clientId: 'mailfathom-client', resource, refreshToken: 'a-refresh-token' };

    it('presents the refresh token it is holding, stating what the fresh token is for', async () => {
        const asked: ClientRequest[] = [];

        await refreshAccessToken(renewal, (request) => {
            asked.push(request);

            return Promise.resolve({
                status: 200,
                body: JSON.stringify({ access_token: 'a-fresh-token', token_type: 'Bearer', refresh_token: 'rotated' }),
                headers: {},
            });
        });

        const body = asked[0]?.body;

        expect(stated(body, 'grant_type')).toBe('refresh_token');
        expect(stated(body, 'refresh_token')).toBe('a-refresh-token');
        expect(stated(body, 'resource')).toBe(resource);
    });

    // A server that rotates refresh tokens answers with a new one, and presenting the old one again is what such a
    // server reads as a stolen token.
    it('answers with the rotated refresh token where the server issued one', async () => {
        const answer = await refreshAccessToken(
            renewal,
            answering({
                body: JSON.stringify({ access_token: 'a-fresh-token', token_type: 'Bearer', refresh_token: 'rotated' }),
            }),
        );

        expect(answer.outcome === 'issued' && answer.token.refreshToken).toBe('rotated');
    });

    it('reads a refused refresh token as the sign-in having ended rather than as something to retry', async () => {
        const answer = await refreshAccessToken(renewal, answering({ status: 400, body: '{"error":"invalid_grant"}' }));

        expect(answer).toEqual({ outcome: 'refused' });
    });
});

describe('revokeRefreshToken', () => {
    it('asks the revocation endpoint to withdraw the token, naming what kind it is', async () => {
        const asked: ClientRequest[] = [];

        await revokeRefreshToken(
            { metadata, clientId: 'mailfathom-client', refreshToken: 'a-refresh-token' },
            (request) => {
                asked.push(request);

                return Promise.resolve({ status: 200, body: '', headers: {} });
            },
        );

        const body = asked[0]?.body;

        expect(asked[0]?.path).toBe(`${issuer}/revoke`);
        expect(stated(body, 'token')).toBe('a-refresh-token');
        expect(stated(body, 'token_type_hint')).toBe('refresh_token');
    });

    it('asks nothing of a server that publishes no revocation endpoint', async () => {
        const asked: ClientRequest[] = [];

        await revokeRefreshToken(
            { metadata: { ...metadata, revocationEndpoint: null }, clientId: 'mailfathom-client', refreshToken: 'a' },
            (request) => {
                asked.push(request);

                return Promise.resolve({ status: 200, body: '', headers: {} });
            },
        );

        expect(asked).toEqual([]);
    });

    // Signing out is complete when the head has forgotten what it kept, so a server that refused to hear about it must
    // not be what a caller waits on or acts upon.
    it('answers even where the server could not be reached', async () => {
        await expect(
            revokeRefreshToken({ metadata, clientId: 'mailfathom-client', refreshToken: 'a' }, () =>
                Promise.reject(new TypeError('no route')),
            ),
        ).resolves.toBeUndefined();
    });
});
