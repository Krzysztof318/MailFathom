// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientResponse } from '@mailfathom/client-backend';
import {
    grantFromIssuedToken,
    grantRenewalMargin,
    readOAuthGrant,
    renewOAuthGrant,
    renewalIsDue,
    writeOAuthGrant,
    type OAuthGrant,
} from './oauthGrant';

const issuer = 'https://id.example.invalid';

const grant: OAuthGrant = {
    authorization: 'Bearer a-token',
    expiresAt: '2026-09-14T12:00:00.000Z',
    refreshToken: 'a-refresh-token',
    issuer,
    clientId: 'mailfathom-client',
    resource: 'https://mail.example.invalid',
    person: 'Ada',
};

const discovered = JSON.stringify({
    issuer,
    authorization_endpoint: `${issuer}/authorize`,
    token_endpoint: `${issuer}/token`,
});

/** A server that publishes its discovery document and answers the token endpoint with whatever the test states. */
function server(token: Partial<ClientResponse>): (request: ClientRequest) => Promise<ClientResponse> {
    return (request) =>
        Promise.resolve(
            request.path.endsWith('/token')
                ? { status: 200, body: '', headers: {}, ...token }
                : { status: 200, body: discovered, headers: {} },
        );
}

describe('readOAuthGrant', () => {
    // A real authorization server issues a signed token of a thousand characters and more, while a session this
    // deployment minted is eighty — so a grant read against a minted session's bound is a sign-in that works for the
    // run it was made in and is silently gone at the next start, refresh token and all.
    it('reads back a grant carrying a token the size an authorization server actually issues', () => {
        const issued = { ...grant, authorization: `Bearer ${'a'.repeat(2000)}` };

        expect(readOAuthGrant(writeOAuthGrant(issued))).toEqual(issued);
    });

    it('refuses a token past what any server issues, which is a store somebody filled by hand', () => {
        const absurd = { ...grant, authorization: `Bearer ${'a'.repeat(9000)}` };

        expect(readOAuthGrant(writeOAuthGrant(absurd))).toBeNull();
    });

    it('reads back what was written, field for field', () => {
        expect(readOAuthGrant(writeOAuthGrant(grant))).toEqual(grant);
    });

    // The difference from a kept session, and the one that matters most: an access token that has run out is the
    // ordinary state of a grant nobody used yesterday, and the refresh token beside it is what makes it work again.
    it('reads a grant whose access token has already run out rather than discarding it', () => {
        const expired = writeOAuthGrant({ ...grant, expiresAt: '2000-01-01T00:00:00.000Z' });

        expect(readOAuthGrant(expired)?.refreshToken).toBe('a-refresh-token');
    });

    it('reads a grant the server issued no refresh token for', () => {
        expect(readOAuthGrant(writeOAuthGrant({ ...grant, refreshToken: null }))?.refreshToken).toBeNull();
    });

    it.each([
        ['nothing kept at all', null],
        ['a value that is not a document', 'not json'],
        ['a document that is a list', '[]'],
        ['a header value a request could not carry', JSON.stringify({ ...grant, authorization: 'Bearer a token' })],
        ['an expiry that is not an instant', JSON.stringify({ ...grant, expiresAt: 'someday' })],
        ['a refresh token carrying whitespace', JSON.stringify({ ...grant, refreshToken: 'a token' })],
        ['no issuer to present a renewal to', JSON.stringify({ ...grant, issuer: '' })],
        ['nobody the deployment named', JSON.stringify({ ...grant, person: 123 })],
    ])('refuses %s', (_, stored) => {
        expect(readOAuthGrant(stored)).toBeNull();
    });

    it('refuses a document longer than anything this client wrote, unread', () => {
        expect(readOAuthGrant(JSON.stringify({ ...grant, person: 'a'.repeat(9000) }))).toBeNull();
    });
});

describe('grantFromIssuedToken', () => {
    const about = { issuer, clientId: 'mailfathom-client', resource: 'https://mail.example.invalid', person: 'Ada' };
    const issuedAt = Date.parse('2026-09-14T12:00:00.000Z');

    it('composes the header value and measures the expiry from what the server said', () => {
        const composed = grantFromIssuedToken(
            { accessToken: 'a-token', expiresInSeconds: 600, refreshToken: 'a-refresh-token' },
            about,
            issuedAt,
        );

        expect(composed.authorization).toBe('Bearer a-token');
        expect(composed.expiresAt).toBe('2026-09-14T12:10:00.000Z');
    });

    // RFC 6749 makes `expires_in` optional, and a client assuming forever would present a dead token until the
    // deployment refused one.
    it('assumes an hour where the server said nothing about how long the token lasts', () => {
        const composed = grantFromIssuedToken(
            { accessToken: 'a-token', expiresInSeconds: null, refreshToken: null },
            about,
            issuedAt,
        );

        expect(composed.expiresAt).toBe('2026-09-14T13:00:00.000Z');
    });

    it('keeps the refresh token already held where the server issued none with the fresh access token', () => {
        const composed = grantFromIssuedToken(
            { accessToken: 'a-token', expiresInSeconds: 600, refreshToken: null },
            { ...about, refreshToken: 'a-refresh-token' },
            issuedAt,
        );

        expect(composed.refreshToken).toBe('a-refresh-token');
    });

    it('takes the rotated refresh token over the one that was presented', () => {
        const composed = grantFromIssuedToken(
            { accessToken: 'a-token', expiresInSeconds: 600, refreshToken: 'rotated' },
            { ...about, refreshToken: 'a-refresh-token' },
            issuedAt,
        );

        expect(composed.refreshToken).toBe('rotated');
    });
});

describe('renewalIsDue', () => {
    const expiresAt = Date.parse(grant.expiresAt);

    it.each([
        ['well before the margin', expiresAt - grantRenewalMargin - 1000, false],
        ['at the margin', expiresAt - grantRenewalMargin, true],
        ['past the expiry', expiresAt + 1000, true],
    ])('reads a grant %s as %s', (_, at, due) => {
        expect(renewalIsDue(grant, at)).toBe(due);
    });
});

describe('renewOAuthGrant', () => {
    it('renews against the server that issued it, carrying the rotated token forward', async () => {
        const renewal = await renewOAuthGrant(
            grant,
            server({ body: JSON.stringify({ access_token: 'fresh', token_type: 'Bearer', refresh_token: 'rotated' }) }),
        );

        expect(renewal.outcome === 'renewed' && renewal.grant.authorization).toBe('Bearer fresh');
        expect(renewal.outcome === 'renewed' && renewal.grant.refreshToken).toBe('rotated');
        expect(renewal.outcome === 'renewed' && renewal.grant.person).toBe('Ada');
    });

    // A refused refresh token is the sign-in being over — signed out elsewhere, or the grant withdrawn — and what
    // follows is the sign-in screen rather than another attempt.
    it('reads a refused refresh token as the grant having ended', async () => {
        const renewal = await renewOAuthGrant(grant, server({ status: 400, body: '{"error":"invalid_grant"}' }));

        expect(renewal.outcome).toBe('ended');
    });

    it('reports a grant with nothing to present as ended without asking anything', async () => {
        const asked: ClientRequest[] = [];

        const renewal = await renewOAuthGrant({ ...grant, refreshToken: null }, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: '', headers: {} });
        });

        expect(renewal.outcome).toBe('ended');
        expect(asked).toEqual([]);
    });

    // Nothing about an unreachable discovery document says the grant was withdrawn, so it is retried rather than ended.
    it('reads a server it could not read as a failure to retry rather than as an ended grant', async () => {
        const renewal = await renewOAuthGrant(grant, () => Promise.reject(new TypeError('no route')));

        expect(renewal.outcome).toBe('failed');
    });
});
