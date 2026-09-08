// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    endSession,
    parseMintedSession,
    renewSession,
    sessionExchangeRoute,
    sessionRevocationRoute,
} from './sessionToken';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session = { baseAddress: 'https://mail.example.invalid', authorization: 'Bearer mfs_abcdef.ghijkl' };

const mintedToken = 'mfs_qrstuvwxyzabcdef.cXVpY2stYnJvd24tZm94LWp1bXBz';
const mintedExpiry = '2026-09-09T09:00:00+00:00';

function answering(response: Partial<ClientResponse>): MailFathomTransport {
    return () => Promise.resolve({ status: 200, body: '', headers: {}, ...response });
}

function minted(fields: Record<string, unknown>): string {
    return JSON.stringify({ token: mintedToken, expiresAt: mintedExpiry, ...fields });
}

describe('renewSession', () => {
    it('presents the token it holds to the exchange, which is the route that answers a fresh one', async () => {
        const asked: ClientRequest[] = [];

        await renewSession(session, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: minted({}), headers: {} });
        });

        expect(asked).toEqual([
            {
                method: 'POST',
                path: `https://mail.example.invalid/api/client${sessionExchangeRoute}`,
                headers: { Accept: 'application/json', Authorization: session.authorization },
            },
        ]);
    });

    it('reads the session that came back as the one to hold from now on', async () => {
        const result = await renewSession(session, answering({ body: minted({}) }));

        expect(result).toEqual({ outcome: 'read', value: { token: mintedToken, expiresAt: mintedExpiry } });
    });

    // The one refusal that means the session is over rather than that the renewal did not get through, which is what
    // tells a client to ask for a password again instead of trying a minute later.
    it('reads a refused renewal as this session no longer being accepted', async () => {
        const result = await renewSession(session, answering({ status: 401 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });

    it('reports a deployment that never answered as one to try again rather than as a session that ended', async () => {
        const result = await renewSession(session, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('refuses an answer that carried no session rather than holding whatever it said', async () => {
        const result = await renewSession(session, answering({ body: '{}' }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('endSession', () => {
    it('asks the revocation route to stop the token it holds working', async () => {
        const asked: ClientRequest[] = [];

        await endSession(session, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 204, body: '', headers: {} });
        });

        expect(asked).toEqual([
            {
                method: 'POST',
                path: `https://mail.example.invalid/api/client${sessionRevocationRoute}`,
                headers: { Accept: 'application/json', Authorization: session.authorization },
            },
        ]);
    });

    it('reads the deployment having ended it as nothing more to report', async () => {
        const result = await endSession(session, answering({ status: 204 }));

        expect(result).toEqual({ outcome: 'read', value: null });
    });

    it('reports a deployment that never heard, which is what leaves the token live until it expires', async () => {
        const result = await endSession(session, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('parseMintedSession', () => {
    it('reads the two fields a deployment answers an exchange with', () => {
        expect(parseMintedSession(minted({}))).toEqual({ token: mintedToken, expiresAt: mintedExpiry });
    });

    it.each([
        ['a body that is not JSON at all', 'not json'],
        ['a body that is not an object', '"a token"'],
        ['an array, which JSON calls an object and this does not', '[]'],
        ['a token that is not a string', JSON.stringify({ token: 7, expiresAt: mintedExpiry })],
        [
            'an empty token, which is a credential naming nothing',
            JSON.stringify({ token: '', expiresAt: mintedExpiry }),
        ],
        ['no instant at all', JSON.stringify({ token: mintedToken })],
        ['an instant no clock reads', JSON.stringify({ token: mintedToken, expiresAt: 'whenever' })],
    ])('answers nothing for %s', (_, body) => {
        expect(parseMintedSession(body)).toBeNull();
    });

    // The bound is on the credential rather than on the reading: a token past what any deployment mints is refused
    // here rather than kept and presented on every later request.
    it('refuses a token past the length any deployment mints', () => {
        expect(parseMintedSession(minted({ token: `mfs_${'a'.repeat(300)}` }))).toBeNull();
    });

    // The token becomes an `Authorization` header verbatim and is kept, so a value a header may not carry would be
    // refused by the browser on every later request — reported as a deployment that cannot be reached, with the
    // unusable session persisted across reloads.
    it.each([
        ['a header break', 'mfs_abc.def\r\nX-Injected: yes'],
        ['a space', 'mfs_abc def'],
        ['a character outside the bearer alphabet', 'mfs_abc.déf'],
    ])('refuses a token carrying %s', (_, token) => {
        expect(parseMintedSession(minted({ token }))).toBeNull();
    });

    it('refuses a body too long to be an exchange answer without expanding it', () => {
        expect(parseMintedSession(minted({ padding: 'a'.repeat(5000) }))).toBeNull();
    });
});
