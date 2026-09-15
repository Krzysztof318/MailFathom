// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse } from '@mailfathom/client-backend';
import { completeOAuthSignIn, startOAuthSignIn, type OAuthSignInStart } from './oauthFlow';
import type { SignInRedirect, SignInRedirectAnswer } from '../shellOperations/signInRedirect';

const issuer = 'https://id.example.invalid';
const deployment = { baseAddress: 'https://mail.example.invalid' };
const redirectUri = 'http://127.0.0.1:8766/';

const server = { issuer, name: 'keycloak', displayName: 'Keycloak', clientId: 'mailfathom-client' };
const resource = { resource: deployment.baseAddress, scopes: ['mailfathom.read'] };

/** What the deployment answers about the person a token belongs to, and what the server answers at each address. */
interface Deployment {
    readonly openId?: boolean;
    readonly token?: Partial<ClientResponse>;
    readonly whoTheyAre?: Partial<ClientResponse>;
}

function answering(said: Deployment = {}): {
    transport: (request: ClientRequest) => Promise<ClientResponse>;
    asked: ClientRequest[];
} {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            if (request.path.endsWith('/display-name')) {
                return Promise.resolve({
                    status: 200,
                    body: JSON.stringify({ displayName: 'Ada Lovelace', changeable: true }),
                    headers: {},
                    ...said.whoTheyAre,
                });
            }

            if (request.path.endsWith('/token')) {
                return Promise.resolve({
                    status: 200,
                    body: JSON.stringify({ access_token: 'a-token', token_type: 'Bearer', expires_in: 3600 }),
                    headers: {},
                    ...said.token,
                });
            }

            return Promise.resolve({
                status: 200,
                headers: {},
                body: JSON.stringify({
                    issuer,
                    authorization_endpoint: `${issuer}/authorize`,
                    token_endpoint: `${issuer}/token`,
                    ...(said.openId === true ? { userinfo_endpoint: `${issuer}/userinfo` } : {}),
                }),
            });
        },
    };
}

/** A head that comes back with the answer itself, which is the shell's way of answering and the one a test can drive. */
function comingBackWith(answer: SignInRedirectAnswer | null): SignInRedirect & { handed: string[] } {
    const handed: string[] = [];

    return {
        handed,
        offered: true,
        redirectUri,
        hand: (address) => {
            handed.push(address);

            return Promise.resolve(answer === null ? null : { ...answer, state: statedIn(address, 'state') });
        },
        abandon: () => undefined,
        answerWaiting: () => null,
    };
}

/**
 * The web head, which is replaced by the address it opens and therefore never answers at all.
 *
 * Distinct from a head answering `null`: that one is the shell saying it could not open the browser or that nobody came
 * back, which ends the sign-in and the written attempt with it. This one has handed over and the answer arrives at the
 * next start of the client.
 */
function replacingTheDocument(): SignInRedirect & { handed: string[] } {
    const handed: string[] = [];

    return {
        handed,
        offered: true,
        redirectUri,
        hand: (address) => {
            handed.push(address);

            return new Promise<SignInRedirectAnswer | null>(() => undefined);
        },
        abandon: () => undefined,
        answerWaiting: () => null,
    };
}

/** What one parameter of an authorization request says, read the way the server reading it would. */
function statedIn(address: string, parameter: string): string {
    return new URL(address).searchParams.get(parameter) ?? '';
}

function starting(
    start: Partial<OAuthSignInStart> & Pick<OAuthSignInStart, 'redirect' | 'transport'>,
): OAuthSignInStart {
    return { deployment, server, resource, ...start };
}

afterEach(() => {
    window.sessionStorage.clear();
    vi.restoreAllMocks();
});

describe('startOAuthSignIn', () => {
    it('hands the person to the server with the code flow, the challenge, and what the token is for', async () => {
        const redirect = comingBackWith({ answered: 'code', code: 'a-code', state: '' });

        await startOAuthSignIn(starting({ redirect, transport: answering().transport }));

        const address = redirect.handed[0] ?? '';

        expect(address.startsWith(`${issuer}/authorize?`)).toBe(true);
        expect(statedIn(address, 'response_type')).toBe('code');
        expect(statedIn(address, 'client_id')).toBe('mailfathom-client');
        expect(statedIn(address, 'redirect_uri')).toBe(redirectUri);
        expect(statedIn(address, 'code_challenge_method')).toBe('S256');
        expect(statedIn(address, 'resource')).toBe(deployment.baseAddress);
        expect(statedIn(address, 'scope')).toBe('mailfathom.read');
    });

    // A provider that issues an identity token is required to bind it to a nonce and some refuse a request without
    // one; a server that is not a provider is sent no parameter it never asked for.
    it('sends a nonce only where the server publishes itself as an OpenID provider', async () => {
        const toAProvider = comingBackWith({ answered: 'code', code: 'a-code', state: '' });
        const toAServer = comingBackWith({ answered: 'code', code: 'a-code', state: '' });

        await startOAuthSignIn(starting({ redirect: toAProvider, transport: answering({ openId: true }).transport }));
        await startOAuthSignIn(starting({ redirect: toAServer, transport: answering().transport }));

        expect(statedIn(toAProvider.handed[0] ?? '', 'nonce')).not.toBe('');
        expect(statedIn(toAServer.handed[0] ?? '', 'nonce')).toBe('');
    });

    it('writes the verifier down for the second half and states it nowhere in the address', async () => {
        const redirect = replacingTheDocument();

        void startOAuthSignIn(starting({ redirect, transport: answering().transport }));

        await vi.waitFor(() => {
            expect(redirect.handed).toHaveLength(1);
        });

        const written = JSON.parse(window.sessionStorage.getItem('mailfathom.signIn.attempt') ?? 'null') as {
            codeVerifier: string;
            state: string;
        } | null;
        const address = redirect.handed[0] ?? '';

        expect(written?.codeVerifier).toMatch(/^[A-Za-z0-9\-_]{43}$/u);
        expect(statedIn(address, 'state')).toBe(written?.state);
        expect(address).not.toContain(written?.codeVerifier);
        expect(statedIn(address, 'code_verifier')).toBe('');
    });

    it('signs in where this head came back with the answer itself, taking the name the deployment knows', async () => {
        const outcome = await startOAuthSignIn(
            starting({
                redirect: comingBackWith({ answered: 'code', code: 'a-code', state: '' }),
                transport: answering().transport,
            }),
        );

        expect(outcome.outcome === 'signedIn' && outcome.grant.person).toBe('Ada Lovelace');
        expect(outcome.outcome === 'signedIn' && outcome.grant.authorization).toBe('Bearer a-token');
        expect(outcome.outcome === 'signedIn' && outcome.grant.issuer).toBe(issuer);
    });

    // Handing somebody to a server whose answer nothing could redeem would spend their password on nothing.
    it('starts nothing where the attempt could not be written down', async () => {
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new DOMException('the quota is exceeded', 'QuotaExceededError');
        });

        const redirect = comingBackWith({ answered: 'code', code: 'a-code', state: '' });
        const outcome = await startOAuthSignIn(starting({ redirect, transport: answering().transport }));

        expect(outcome).toEqual({ outcome: 'refused', refusal: 'unavailable' });
        expect(redirect.handed).toEqual([]);
    });

    it('starts nothing where the server published no document to compose a request from', async () => {
        const redirect = comingBackWith({ answered: 'code', code: 'a-code', state: '' });

        const outcome = await startOAuthSignIn(
            starting({ redirect, transport: () => Promise.reject(new TypeError('no route')) }),
        );

        expect(outcome).toEqual({ outcome: 'refused', refusal: 'unavailable' });
        expect(redirect.handed).toEqual([]);
    });

    // The shell could not open the browser, or nobody came back inside the wait it holds the redirect port for. The
    // verifier goes with the sign-in rather than outliving it: the authorization request did reach the server, and a
    // secret left behind is one a later run of this tab would find.
    it('puts somebody back on the sign-in screen where the head came back with nothing at all', async () => {
        const outcome = await startOAuthSignIn(
            starting({ redirect: comingBackWith(null), transport: answering().transport }),
        );

        expect(outcome).toEqual({ outcome: 'refused', refusal: 'unavailable' });
        expect(window.sessionStorage.getItem('mailfathom.signIn.attempt')).toBeNull();
    });
});

describe('completeOAuthSignIn', () => {
    /** Starts an attempt on the head that is replaced by what it opens, so the second half can be driven on its own. */
    async function attemptInFlight(): Promise<string> {
        const redirect = replacingTheDocument();

        void startOAuthSignIn(starting({ redirect, transport: answering().transport }));

        await vi.waitFor(() => {
            expect(redirect.handed).toHaveLength(1);
        });

        return statedIn(redirect.handed[0] ?? '', 'state');
    }

    it('redeems the code against the attempt this tab wrote down', async () => {
        const state = await attemptInFlight();
        const { transport, asked } = answering();

        const outcome = await completeOAuthSignIn({ answered: 'code', code: 'a-code', state }, transport);

        expect(outcome.outcome).toBe('signedIn');
        expect(new URLSearchParams(asked.find((request) => request.path.endsWith('/token'))?.body).get('code')).toBe(
            'a-code',
        );
    });

    it('redeems nothing for an answer this client did not start', async () => {
        await attemptInFlight();
        const { transport, asked } = answering();

        const outcome = await completeOAuthSignIn(
            { answered: 'code', code: 'a-code', state: 'somebody-else' },
            transport,
        );

        expect(outcome).toEqual({ outcome: 'refused', refusal: 'unexpectedAnswer' });
        expect(asked).toEqual([]);
    });

    // A verifier left behind is a secret outliving the one code it was for, and an answer arriving twice must redeem
    // nothing the second time.
    it('takes the written attempt once, so the same answer replayed redeems nothing', async () => {
        const state = await attemptInFlight();
        const answer = { answered: 'code', code: 'a-code', state } as const;

        await completeOAuthSignIn(answer, answering().transport);

        expect(await completeOAuthSignIn(answer, answering().transport)).toEqual({
            outcome: 'refused',
            refusal: 'unexpectedAnswer',
        });
        expect(window.sessionStorage.getItem('mailfathom.signIn.attempt')).toBeNull();
    });

    it('reads a server that refused the authorization as nobody having authorized it', async () => {
        const state = await attemptInFlight();

        expect(await completeOAuthSignIn({ answered: 'refused', state }, answering().transport)).toEqual({
            outcome: 'refused',
            refusal: 'notAuthorized',
        });
    });

    it('reads a refused redemption as the server refusing rather than as a failure to reach it', async () => {
        const state = await attemptInFlight();
        const { transport } = answering({ token: { status: 400, body: '{"error":"invalid_grant"}' } });

        expect(await completeOAuthSignIn({ answered: 'code', code: 'a-code', state }, transport)).toEqual({
            outcome: 'refused',
            refusal: 'refused',
        });
    });

    // The deployment is the one party that knows what the token means here, and an empty `401` is it saying the
    // subject maps to no user of its own — a sign-in that has not happened however good the token is.
    it('refuses a token whose subject the deployment maps to nobody', async () => {
        const state = await attemptInFlight();
        const { transport } = answering({ whoTheyAre: { status: 401, body: '' } });

        expect(await completeOAuthSignIn({ answered: 'code', code: 'a-code', state }, transport)).toEqual({
            outcome: 'refused',
            refusal: 'notAUser',
        });
    });

    // A deployment that declined this one read has not said the token is unknown, and signing somebody out over a name
    // is a worse answer than a name that is the provider's.
    it('keeps the provider’s own name where the deployment could not answer who this is', async () => {
        const state = await attemptInFlight();
        const { transport } = answering({ whoTheyAre: { status: 503, body: '' } });

        const outcome = await completeOAuthSignIn({ answered: 'code', code: 'a-code', state }, transport);

        expect(outcome.outcome === 'signedIn' && outcome.grant.person).toBe('Keycloak');
    });
});
