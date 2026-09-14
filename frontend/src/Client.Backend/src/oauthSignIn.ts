// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { isSecureAddress, isSecureEndpoint, originOf } from './signInMethods';
import { reported, spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// The authorization server half of signing in, which is the one wire in this client that is not MailFathom's. The
// deployment is a protected resource and nothing else: it issues no token, redeems no code, and serves no login page,
// so every request in this module goes to somebody else's server and none of it passes through the deployment.
//
// Authorization code with PKCE, and nothing beside it. The implicit and hybrid flows put a token in an address bar,
// which is a credential in the browser's history, in the referrer of whatever the page loads next, and in every proxy
// log on the way — and neither is needed by a client that can hold a verifier.
//
// What this module does *not* do is anything a browser has to do: there is no random here, no digest, and no
// navigation, because this package declares no DOM. It composes the address to send somebody to out of values a caller
// generated, and it reads what comes back from the token endpoint. `Client.App/src/signIn/` is where the platform's
// cryptographically secure generator is reached and where the address bar is.

/** Where an authorization server is reached, as its own discovery document reports it. */
export interface AuthorizationServerMetadata {
    /** The issuer the document claims, which is compared against the one the deployment published before anything is used. */
    readonly issuer: string;

    /** Where a person is sent to authorize. */
    readonly authorizationEndpoint: string;

    /** Where a code is redeemed and a token refreshed. */
    readonly tokenEndpoint: string;

    /** Where a refresh token is given back at sign-out, or `null` where the server publishes no such endpoint. */
    readonly revocationEndpoint: string | null;

    /** Whether the server is an OpenID provider, which is what decides a `nonce` is sent. */
    readonly opensIdentity: boolean;
}

/** The most of a discovery document this package reads, which is far past what any server writes. */
export const longestDiscoveryAnswer = 131_072;

/** The most of a token answer this package reads. A token is a few kilobytes at the outside, and a refresh token less. */
export const longestTokenAnswer = 32_768;

/**
 * The most either issued token may be, which is the bound on the values rather than on the document carrying them.
 *
 * Past every signed token a real server issues — a JWT carrying a group claim for each of somebody's teams is two or
 * three kilobytes — and short of what the deployment would accept in a request header anyway. It is also what the
 * client stores a grant under, so a token accepted here is one the next start can read back rather than one that signs
 * somebody in for a run and is silently gone.
 */
export const longestIssuedToken = 8_192;

/**
 * The longest life this client will believe, in seconds, which is a year.
 *
 * `expires_in` is a number a server wrote and nothing else checks it. What is computed from it is an instant, and an
 * instant past what a `Date` can hold is a `RangeError` out of the composition rather than a refusal — so the bound is
 * here, and a server claiming more is read as having said nothing, which is the assumed hour.
 */
const longestTokenLifeSeconds = 31_536_000;

/**
 * Reads an authorization server's own discovery document, given only the issuer the deployment published.
 *
 * The candidates are the order the MCP authorization specification names — the OAuth 2.0 Authorization Server Metadata
 * address first, then the two OpenID Connect Discovery addresses — and the first that answers with a document claiming
 * this issuer is the one taken. That is the same order the deployment itself uses to find the key set it validates
 * tokens against, so a client and a service reading one configured issuer reach one server.
 *
 * Every candidate is derived from the issuer, so nothing here can be made to fetch an address the deployment did not
 * publish, and every endpoint read out of the document is checked against the issuer's own origin for the same reason:
 * a document is a server's own statement about itself and not a redirection to somewhere else.
 *
 * @param issuer The issuer identifier the deployment published.
 * @param transport How the request goes out.
 * @returns Where the server is reached, or why there was nothing to read.
 */
export function readAuthorizationServer(
    issuer: string,
    transport: MailFathomTransport,
): Promise<ClientResult<AuthorizationServerMetadata>> {
    return spanned('GET authorization server metadata', async () => {
        if (!isSecureAddress(issuer)) {
            return failed<AuthorizationServerMetadata>('unreadable', null);
        }

        let reached = false;

        for (const candidate of discoveryAddresses(issuer)) {
            const response = await send(transport, {
                method: 'GET',
                path: candidate,
                headers: { Accept: 'application/json' },
                longestAnswer: longestDiscoveryAnswer,
            });

            if (response === null) {
                continue;
            }

            reached = true;

            if (response.status !== 200) {
                continue;
            }

            const metadata = parseMetadata(response.body, issuer);

            if (metadata !== null) {
                return read(metadata);
            }
        }

        // A server none of the three candidates reached is one this client could not talk to; one that answered and
        // published nothing usable is a configuration nobody can act on from a screen. The two are separated because a
        // person retries the first and reports the second.
        return failed<AuthorizationServerMetadata>(reached ? 'unreadable' : 'unavailable', null);
    });
}

/**
 * The addresses an issuer's discovery document is looked for at, most specific specification first.
 *
 * @param issuer An issuer {@link isSecureAddress} has accepted.
 * @returns Two candidates for an issuer with no path, and three for one with a path.
 */
export function discoveryAddresses(issuer: string): readonly string[] {
    const origin = originOf(issuer);
    const path = issuer.slice(origin.length).replace(/\/+$/u, '');

    return path.length === 0
        ? [`${origin}/.well-known/oauth-authorization-server`, `${origin}/.well-known/openid-configuration`]
        : [
              `${origin}/.well-known/oauth-authorization-server${path}`,
              `${origin}/.well-known/openid-configuration${path}`,
              `${origin}${path}/.well-known/openid-configuration`,
          ];
}

/** Everything one authorization request states, which is composed here and sent by whatever owns the address bar. */
export interface AuthorizationRequest {
    readonly metadata: AuthorizationServerMetadata;
    readonly clientId: string;

    /** Where the server sends the person back, which the operator registered at that server exactly as written. */
    readonly redirectUri: string;

    /** What the token has to be issued for, which RFC 8707 carries as `resource`. */
    readonly resource: string;

    /** The scopes to ask for, as the deployment's own protected resource document advertises them. */
    readonly scopes: readonly string[];

    /** The single-use value the answer is matched against, which is what makes a redirect somebody else started unusable. */
    readonly state: string;

    /** The `S256` challenge derived from the verifier the caller generated and kept. */
    readonly codeChallenge: string;

    /** The value an OpenID provider echoes into its identity token, sent only where the server is one. */
    readonly nonce: string | null;
}

/**
 * The address a person is sent to in order to authorize.
 *
 * `offline_access` is not added here and is not assumed: a refresh token is issued because the deployment's own
 * protected resource document advertised that scope and the server honoured it, which is what keeps this client from
 * asking every server for a credential the operator did not mean to hand out.
 *
 * @param request Everything the request states.
 * @returns The absolute address to open.
 */
export function authorizationRequestAddress(request: AuthorizationRequest): string {
    const stated: string[] = [
        'response_type=code',
        `client_id=${encodeURIComponent(request.clientId)}`,
        `redirect_uri=${encodeURIComponent(request.redirectUri)}`,
        `state=${encodeURIComponent(request.state)}`,
        `code_challenge=${encodeURIComponent(request.codeChallenge)}`,
        'code_challenge_method=S256',
        `resource=${encodeURIComponent(request.resource)}`,
    ];

    if (request.scopes.length > 0) {
        stated.push(`scope=${encodeURIComponent(request.scopes.join(' '))}`);
    }

    if (request.nonce !== null) {
        stated.push(`nonce=${encodeURIComponent(request.nonce)}`);
    }

    // An authorization endpoint is permitted to carry a query of its own, and a server that publishes one means it.
    const separator = request.metadata.authorizationEndpoint.includes('?') ? '&' : '?';

    return `${request.metadata.authorizationEndpoint}${separator}${stated.join('&')}`;
}

/** What an authorization server issued, as this client keeps it. */
export interface IssuedAccessToken {
    /** The token itself, presented as the bearer credential on every request to the deployment. */
    readonly accessToken: string;

    /** How long the token is good for, in seconds, or `null` where the server said nothing about it. */
    readonly expiresInSeconds: number | null;

    /**
     * The refresh token, or `null` where the server issued none.
     *
     * A rotated one arrives here on every refresh and replaces the one that was presented, which is what a server
     * rotating them requires: presenting a rotated token twice is what such a server reads as a stolen one.
     */
    readonly refreshToken: string | null;
}

/** What came of asking an authorization server for a token. */
export type TokenOutcome =
    | { readonly outcome: 'issued'; readonly token: IssuedAccessToken }

    /** The server read what was presented and refused it: a code already spent, a verifier that does not match, a refresh token it has withdrawn. */
    | { readonly outcome: 'refused' }
    | { readonly outcome: 'failed'; readonly failure: { readonly reason: 'unavailable' | 'unreadable' } };

/**
 * Redeems an authorization code for a token, presenting the verifier the request was started with.
 *
 * The request goes from this client straight to the authorization server: no code, no token, and no refresh token
 * passes through MailFathom, and nothing about this arrangement makes the deployment hold one. There is no client
 * secret either — a page cannot keep one — which is what PKCE replaces.
 *
 * @param redemption The server, the client, and the code with the verifier that was kept for it.
 * @param transport How the request goes out.
 * @returns The token, the server's refusal, or why nothing answered.
 */
export function redeemAuthorizationCode(
    redemption: {
        readonly metadata: AuthorizationServerMetadata;
        readonly clientId: string;
        readonly redirectUri: string;
        readonly resource: string;
        readonly code: string;
        readonly codeVerifier: string;
    },
    transport: MailFathomTransport,
): Promise<TokenOutcome> {
    return askForToken(
        redemption.metadata.tokenEndpoint,
        [
            'grant_type=authorization_code',
            `code=${encodeURIComponent(redemption.code)}`,
            `redirect_uri=${encodeURIComponent(redemption.redirectUri)}`,
            `client_id=${encodeURIComponent(redemption.clientId)}`,
            `code_verifier=${encodeURIComponent(redemption.codeVerifier)}`,
            `resource=${encodeURIComponent(redemption.resource)}`,
        ],
        transport,
    );
}

/**
 * Asks for a fresh access token with the refresh token this client is holding.
 *
 * A server that refuses it has ended the sign-in — the person signed out somewhere else, an administrator withdrew the
 * grant, or the token simply aged out — and the answer is to put somebody in front of the sign-in screen rather than to
 * try again. That is the whole reason a refusal is a value here and not a failure.
 *
 * @param renewal The server, the client, and the refresh token being presented.
 * @param transport How the request goes out.
 * @returns The token, the server's refusal, or why nothing answered.
 */
export function refreshAccessToken(
    renewal: {
        readonly metadata: AuthorizationServerMetadata;
        readonly clientId: string;
        readonly resource: string;
        readonly refreshToken: string;
    },
    transport: MailFathomTransport,
): Promise<TokenOutcome> {
    return askForToken(
        renewal.metadata.tokenEndpoint,
        [
            'grant_type=refresh_token',
            `refresh_token=${encodeURIComponent(renewal.refreshToken)}`,
            `client_id=${encodeURIComponent(renewal.clientId)}`,
            `resource=${encodeURIComponent(renewal.resource)}`,
        ],
        transport,
    );
}

/**
 * Asks the authorization server to withdraw the refresh token this client was holding.
 *
 * Asked and not waited on, exactly as the deployment's own session revocation is: signing out is complete when the head
 * has forgotten what it kept, and a server that is unreachable at that moment must not be what keeps somebody signed
 * in on the screen. A server publishing no revocation endpoint is asked nothing.
 *
 * @param revocation The server, the client, and the refresh token to withdraw.
 * @param transport How the request goes out.
 * @returns Nothing; whatever the server said is not something a screen acts on.
 */
export async function revokeRefreshToken(
    revocation: {
        readonly metadata: AuthorizationServerMetadata;
        readonly clientId: string;
        readonly refreshToken: string;
    },
    transport: MailFathomTransport,
): Promise<void> {
    const endpoint = revocation.metadata.revocationEndpoint;

    if (endpoint === null) {
        return;
    }

    await reported(
        'POST revocation endpoint',
        async () => {
            await send(transport, {
                method: 'POST',
                path: endpoint,
                headers: { Accept: 'application/json', 'Content-Type': 'application/x-www-form-urlencoded' },
                body: [
                    `token=${encodeURIComponent(revocation.refreshToken)}`,
                    'token_type_hint=refresh_token',
                    `client_id=${encodeURIComponent(revocation.clientId)}`,
                ].join('&'),
                longestAnswer: longestTokenAnswer,
            });

            return null;
        },

        // Nothing it could answer is acted on, so nothing it answers is a failure to record: the sign-out already
        // happened in the head, and a server that refused to hear about it changes none of that.
        () => null,
    );
}

function askForToken(
    endpoint: string,
    stated: readonly string[],
    transport: MailFathomTransport,
): Promise<TokenOutcome> {
    return reported('POST token endpoint', askedFor, (outcome) =>
        outcome.outcome === 'failed' ? outcome.failure.reason : null,
    );

    async function askedFor(): Promise<TokenOutcome> {
        const response = await send(transport, {
            method: 'POST',
            path: endpoint,
            headers: { Accept: 'application/json', 'Content-Type': 'application/x-www-form-urlencoded' },
            body: stated.join('&'),
            longestAnswer: longestTokenAnswer,
        });

        if (response === null) {
            return { outcome: 'failed', failure: { reason: 'unavailable' } } as const;
        }

        // RFC 6749 answers a refused grant with `400` and an `error` in the body, and a server that authenticated the
        // client badly with `401`. Both are the same sentence to somebody on the screen: this sign-in did not happen,
        // start it again.
        if (response.status === 400 || response.status === 401) {
            return { outcome: 'refused' } as const;
        }

        if (response.status !== 200) {
            return { outcome: 'failed', failure: { reason: 'unavailable' } } as const;
        }

        const token = parseIssuedToken(response.body);

        return token === null
            ? ({ outcome: 'failed', failure: { reason: 'unreadable' } } as const)
            : ({ outcome: 'issued', token } as const);
    }
}

function parseMetadata(body: string, issuer: string): AuthorizationServerMetadata | null {
    let document: Readonly<Record<string, unknown>> | null;

    try {
        document = asRecord(JSON.parse(body));
    } catch {
        return null;
    }

    if (document?.['issuer'] !== issuer) {
        return null;
    }

    const authorizationEndpoint = endpointOn(document['authorization_endpoint'], issuer);
    const tokenEndpoint = endpointOn(document['token_endpoint'], issuer);

    if (authorizationEndpoint === null || tokenEndpoint === null) {
        return null;
    }

    return {
        issuer,
        authorizationEndpoint,
        tokenEndpoint,
        revocationEndpoint: endpointOn(document['revocation_endpoint'], issuer),

        // An OpenID provider is recognized by the document it publishes rather than by which of the three addresses
        // answered, because a server may publish the same document at all of them. What it decides is one thing: a
        // `nonce` is sent, because a provider that issues an identity token is required to bind it to one.
        opensIdentity:
            typeof document['userinfo_endpoint'] === 'string' ||
            Array.isArray(document['id_token_signing_alg_values_supported']),
    };
}

/**
 * An endpoint the document names, accepted only where it sits on the issuer's own origin.
 *
 * A discovery document is a server's statement about itself. One naming somewhere else is either misconfigured or is a
 * server being used to point this client at an address the deployment never published — and the content security policy
 * the page is served under admits the issuer's origin and no other, so such an address would be refused before it left
 * anyway. Refusing it here is what turns that into a readable outcome rather than a request that silently never went.
 *
 * Read with {@link isSecureEndpoint} rather than with the check an issuer takes, because an endpoint is permitted a
 * query of its own and a server publishing one means it — which is the case the separator below is composed for.
 */
function endpointOn(value: unknown, issuer: string): string | null {
    if (typeof value !== 'string' || !isSecureEndpoint(value)) {
        return null;
    }

    return originOf(value) === originOf(issuer) ? value : null;
}

function parseIssuedToken(body: string): IssuedAccessToken | null {
    let document: Readonly<Record<string, unknown>> | null;

    try {
        document = asRecord(JSON.parse(body));
    } catch {
        return null;
    }

    if (document === null) {
        return null;
    }

    const accessToken = document['access_token'];
    const tokenType = document['token_type'];
    const expiresIn = document['expires_in'];
    const refreshToken = document['refresh_token'];

    if (typeof accessToken !== 'string' || accessToken.length === 0 || accessToken.length > longestIssuedToken) {
        return null;
    }

    // The one field this client would act on wrongly if it were something else: a token of another type is not a value
    // to put behind `Bearer`. RFC 6749 fixes the name case-insensitively.
    if (typeof tokenType !== 'string' || tokenType.toLowerCase() !== 'bearer') {
        return null;
    }

    return {
        accessToken,
        expiresInSeconds:
            typeof expiresIn === 'number' && expiresIn > 0 && expiresIn <= longestTokenLifeSeconds ? expiresIn : null,
        refreshToken:
            typeof refreshToken === 'string' && refreshToken.length > 0 && refreshToken.length <= longestIssuedToken
                ? refreshToken
                : null,
    };
}
