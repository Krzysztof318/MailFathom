// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What this client holds between starts when somebody signed in with an authorization server, beside what
// `keptSession.ts` holds when they signed in with a password. Two documents rather than one shape with a discriminator,
// because the two have almost nothing in common past the header value: a session is renewed by presenting itself to the
// deployment that minted it, and a grant is renewed by presenting a refresh token to a server the deployment does not
// own.
//
// Seven fields, and the four past the header value are what renewing one needs. The refresh token is what a renewal
// presents. The issuer is which server to present it to, and it is kept rather than looked up because the document
// naming it may have changed by the next start — a deployment that withdrew a provider must not strand the grant it
// already issued in a state where nothing can even revoke it. The client identifier and the resource travel with every
// token request the server will answer. The person's name is here for the reason it is in a kept session: a token names
// nobody, and the client learned who this is by asking the deployment.
//
// **An expired grant is not nothing kept**, which is the one place this read differs from `readKeptSession` and the
// difference that matters most. An expired session is over and the person signs in again; an expired *access token* is
// the ordinary state of a grant that was last used yesterday, and the refresh token beside it is what turns it back
// into a working one without anybody typing. So the instant is read and never refused on.

import {
    longestIssuedToken,
    readAuthorizationServer,
    refreshAccessToken,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { isPresentableIssuedCredential, longestCredentialPart, resolveSessionCredential } from './credentialEntry';

/** The header value, when it stops working, and everything renewing it needs. */
export interface OAuthGrant {
    /** The `Authorization` header value to present, composed by `credentialEntry` exactly as a session's is. */
    readonly authorization: string;

    /** When the access token stops working, as an instant this client computed from what the server said. */
    readonly expiresAt: string;

    /** The refresh token, or `null` where the server issued none and the grant ends with the access token. */
    readonly refreshToken: string | null;

    /** The authorization server that issued it, which is where a renewal and a revocation are presented. */
    readonly issuer: string;

    /** The client identifier the deployment published for that server. */
    readonly clientId: string;

    /** What the token was issued for, per RFC 8707, which every later token request states again. */
    readonly resource: string;

    /** The name the deployment knows the person by, read from the deployment after the token was issued. */
    readonly person: string;
}

/** The most a stored document may be before it is refused unread, which holds both tokens at their own bound and the names beside them. */
const longestKeptGrant = 2 * longestIssuedToken + 2048;

/**
 * How long an access token is assumed to last where the server said nothing about it.
 *
 * `expires_in` is optional in RFC 6749, and a client that assumed forever would present a dead token on every request
 * until the deployment refused one. Assuming an hour costs a refresh nobody needed; assuming forever costs the sign-in.
 */
const assumedTokenLife = 3_600_000;

/** How long before an access token expires that renewing it becomes due. */
export const grantRenewalMargin = 300_000;

/**
 * The stored form of a grant, as one value the store keeps under one entry.
 *
 * @param grant The grant to keep.
 * @returns What to hand the credential store.
 */
export function writeOAuthGrant(grant: OAuthGrant): string {
    return JSON.stringify({
        authorization: grant.authorization,
        expiresAt: grant.expiresAt,
        refreshToken: grant.refreshToken,
        issuer: grant.issuer,
        clientId: grant.clientId,
        resource: grant.resource,
        person: grant.person,
    });
}

/**
 * What a stored value says, or `null` where it is not a grant this client wrote.
 *
 * Refused on the same terms a kept session is and for the same reason — the store is a place any script on the origin
 * can write to, and what is on the far side of this read is the `Headers` constructor — with the one difference the
 * module opens with: the instant is checked for being an instant and never for being in the future.
 *
 * @param stored What the credential store answered with.
 * @returns The grant, or `null`.
 */
export function readOAuthGrant(stored: string | null): OAuthGrant | null {
    if (stored === null || stored.length === 0 || stored.length > longestKeptGrant) {
        return null;
    }

    let parsed: unknown;

    try {
        parsed = JSON.parse(stored);
    } catch {
        return null;
    }

    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        return null;
    }

    const kept = parsed as Record<string, unknown>;
    const authorization = kept['authorization'];
    const expiresAt = kept['expiresAt'];
    const refreshToken = kept['refreshToken'];

    // Against the bound an issued token is accepted under rather than the one a minted session is: what is behind the
    // scheme here came from an authorization server, and a session's bound would refuse every real one.
    if (typeof authorization !== 'string' || !isPresentableIssuedCredential(authorization, longestIssuedToken)) {
        return null;
    }

    if (typeof expiresAt !== 'string' || Number.isNaN(Date.parse(expiresAt))) {
        return null;
    }

    if (refreshToken !== null && (typeof refreshToken !== 'string' || !isToken(refreshToken))) {
        return null;
    }

    const issuer = named(kept['issuer']);
    const clientId = named(kept['clientId']);
    const resource = named(kept['resource']);
    const person = named(kept['person']);

    return issuer === null || clientId === null || resource === null || person === null
        ? null
        : { authorization, expiresAt, refreshToken, issuer, clientId, resource, person };
}

/**
 * The grant a freshly issued token becomes.
 *
 * @param issued What the authorization server answered with.
 * @param about Which server issued it, for whom, and what it was issued for.
 * @param issuedAt The instant the expiry is measured from, which is this machine's clock unless a caller states one.
 * @returns The grant to hold and to keep.
 */
export function grantFromIssuedToken(
    issued: {
        readonly accessToken: string;
        readonly expiresInSeconds: number | null;
        readonly refreshToken: string | null;
    },
    about: {
        readonly issuer: string;
        readonly clientId: string;
        readonly resource: string;
        readonly person: string;
        readonly refreshToken?: string | null;
    },
    issuedAt: number = Date.now(),
): OAuthGrant {
    const life = issued.expiresInSeconds === null ? assumedTokenLife : issued.expiresInSeconds * 1000;

    return {
        authorization: resolveSessionCredential(issued.accessToken),
        expiresAt: new Date(issuedAt + life).toISOString(),

        // A server that rotates refresh tokens sends a new one with every renewal and refuses the old one afterwards;
        // one that does not sends none, and the token already held is still the one to present. Reading an absent
        // answer as "no refresh token" would end every grant on such a server at the first renewal.
        refreshToken: issued.refreshToken ?? about.refreshToken ?? null,
        issuer: about.issuer,
        clientId: about.clientId,
        resource: about.resource,
        person: about.person,
    };
}

/**
 * Whether renewing this grant is due, which is what a caller reads before spending a round trip on one.
 *
 * @param grant The grant being held.
 * @param at The instant to judge against, which is this machine's clock unless a caller states one.
 * @returns Whether the access token is inside {@link grantRenewalMargin} of its expiry, or already past it.
 */
export function renewalIsDue(grant: OAuthGrant, at: number = Date.now()): boolean {
    return Date.parse(grant.expiresAt) - at <= grantRenewalMargin;
}

/** What came of asking the authorization server to renew a grant. */
export type GrantRenewal =
    | { readonly outcome: 'renewed'; readonly grant: OAuthGrant }

    /** The server will not renew it: the person signed out somewhere else, or an administrator withdrew the grant. */
    | { readonly outcome: 'ended' }

    /** Nothing answered, so what is held is kept and the next attempt is the next tick. */
    | { readonly outcome: 'failed' };

/**
 * Renews a grant against the server that issued it, presenting the refresh token this client is holding.
 *
 * The server is rediscovered rather than remembered, because the endpoint a renewal is presented to is the server's own
 * statement about itself and it may have moved since the token was issued — a grant kept for a month outlives more than
 * one such document. A server whose document cannot be read now is a failure to retry, never an ended grant: nothing
 * about it says the grant was withdrawn.
 *
 * @param grant The grant being held.
 * @param transport How the requests go out.
 * @returns The renewed grant, the server's refusal, or that nothing answered.
 */
export async function renewOAuthGrant(grant: OAuthGrant, transport: MailFathomTransport): Promise<GrantRenewal> {
    if (grant.refreshToken === null) {
        // A grant with nothing to present is over at its own expiry, and there is no attempt to make. It is reported as
        // ended rather than failed, because retrying it is what would never stop.
        return { outcome: 'ended' };
    }

    const server = await readAuthorizationServer(grant.issuer, transport);

    if (server.outcome !== 'read') {
        return { outcome: 'failed' };
    }

    const answer = await refreshAccessToken(
        {
            metadata: server.value,
            clientId: grant.clientId,
            resource: grant.resource,
            refreshToken: grant.refreshToken,
        },
        transport,
    );

    switch (answer.outcome) {
        case 'issued':
            return {
                outcome: 'renewed',
                grant: grantFromIssuedToken(answer.token, {
                    issuer: grant.issuer,
                    clientId: grant.clientId,
                    resource: grant.resource,
                    person: grant.person,
                    refreshToken: grant.refreshToken,
                }),
            };
        case 'refused':
            return { outcome: 'ended' };
        default:
            return { outcome: 'failed' };
    }
}

/** A name a grant carries, or `null` where the store answered with something that is not one. */
function named(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestCredentialPart ? value : null;
}

/** Whether a stored token is one worth presenting, which bounds it and refuses whitespace a request body would carry. */
function isToken(value: string): boolean {
    return value.length > 0 && value.length <= longestIssuedToken && !/\s/u.test(value);
}
