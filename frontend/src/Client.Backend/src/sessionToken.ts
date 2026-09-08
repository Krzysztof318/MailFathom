// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What a client presents once it has signed in, and the two routes that begin and end it.
//
// The deployment derives a password once, at the exchange, and hands back a token instead; every request afterwards is
// authenticated by that token at the cost of a lookup rather than of a key derivation. What this package holds about
// it is unchanged from what it held about a password: the finished header value arrives composed and is sent, and
// nothing here takes one apart.
//
// The token has a life the deployment decides, so a client renews before it ends. There is no separate renewal
// credential and no second route: presenting a live token to the exchange answers a fresh one and stops the presented
// one working, which is what keeps one sign-in to one live credential.

/** The route a credential is exchanged for a session, and a session renewed, relative to the client prefix. */
export const sessionExchangeRoute = '/session/token';

/** The route a session is ended at, relative to the client prefix. */
export const sessionRevocationRoute = '/session/token/revocation';

/** The longest exchange answer read at all, which names a token and an instant and is a couple of hundred bytes. */
const longestExchangeBody = 4096;

/** The most a token this client will hold may be, which is far past the seventy characters a deployment mints. */
const longestToken = 256;

/**
 * What a token may be made of, which is the `token68` alphabet RFC 6750 gives a bearer credential.
 *
 * Checked here because this is the one value in the client that becomes a header: what comes back is composed into
 * `Authorization` verbatim and kept, so a token carrying a line break would be refused by the `Headers` constructor on
 * every later request — reported as a deployment that cannot be reached, with the unusable session persisted across
 * reloads and no way out but signing out again.
 */
const tokenAlphabet = /^[A-Za-z0-9\-._~+/]+=*$/;

/** What a deployment answers an exchange with. */
export interface MintedSession {
    /** The token to present, which this package never composes a header from and never takes apart. */
    readonly token: string;

    /**
     * When presenting it stops working, as the deployment wrote it.
     *
     * Kept as the instant the deployment stated rather than as a duration this client measured, because the deployment
     * is what decides when a session ends and nothing here can recompute it. What that costs is a clock: a renewal is
     * this value read against the client's own, so a client running behind the deployment by more than the renewal
     * margin renews after the session is already over, is refused, and asks for a password again.
     */
    readonly expiresAt: string;
}

/**
 * Renews a live session, so somebody stays signed in without typing a password again.
 *
 * @param session The address to reach and the finished header value carrying the token this client holds.
 * @param transport How the request goes out.
 * @returns The token to hold from now on, or why the answer never arrived.
 */
export function renewSession(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<MintedSession>> {
    return spanned(`POST ${sessionExchangeRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, sessionExchangeRoute),
            headers: headersFor(session),
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const minted = parseMintedSession(response.body);

        return minted === null ? failed('unreadable', response.status) : read(minted);
    });
}

/**
 * Ends the session this client is holding, so the token stops working before it would have expired.
 *
 * @param session The address to reach and the finished header value carrying the token to end.
 * @param transport How the request goes out.
 * @returns That the deployment ended it, or why the answer never arrived.
 * @remarks
 * The caller forgets what it kept whatever this answers. A deployment that never heard the sign-out still expires the
 * token on its own, and a person who pressed sign out has signed out of the client either way — so this is what closes
 * the window between the two rather than what makes signing out happen.
 */
export function endSession(session: ClientSession, transport: MailFathomTransport): Promise<ClientResult<null>> {
    return spanned(`POST ${sessionRevocationRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, sessionRevocationRoute),
            headers: headersFor(session),
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        return response.status === 204 ? read(null) : failed(failureReasonForStatus(response.status), response.status);
    });
}

/**
 * What an exchange answer says, or `null` where the body is not one MailFathom writes.
 *
 * Bounded before it is expanded, and bounded and read for its own alphabet on the token itself, because this is the
 * one answer in the client that is a credential: a value past what a deployment mints, or carrying anything a header
 * may not, is refused here rather than carried into storage and onto every later request.
 *
 * @param body The response body as it arrived.
 * @returns The minted session, or `null`.
 */
export function parseMintedSession(body: string): MintedSession | null {
    if (body.length > longestExchangeBody) {
        return null;
    }

    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        return null;
    }

    const answered = parsed as Record<string, unknown>;
    const token = answered['token'];
    const expiresAt = answered['expiresAt'];

    if (typeof token !== 'string' || token.length === 0 || token.length > longestToken || !tokenAlphabet.test(token)) {
        return null;
    }

    // The instant is read as one rather than taken on trust, because what it decides is when this client renews: an
    // unparseable value would schedule a renewal at a time no clock reaches, leaving somebody signed out at the token's
    // own expiry with nothing having tried.
    return typeof expiresAt === 'string' && !Number.isNaN(Date.parse(expiresAt)) ? { token, expiresAt } : null;
}
