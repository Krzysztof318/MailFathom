// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readClientPreferences, unsetClientPreferences, writeClientPreferences } from './clientPreferences';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const storedBody = JSON.stringify({ telemetryEnabled: false, theme: 'dark', openMailInTabs: true });

// The transport is the network boundary and the whole of what a test here fakes. Neither route reads a header off an
// answer, so each helper supplies the empty set.
type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

describe('readClientPreferences', () => {
    it('asks for the preferences route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: storedBody });

        await readClientPreferences(session, transport);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/preferences');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('reads the three preferences the deployment answered', async () => {
        const answer = await readClientPreferences(session, answering({ status: 200, body: storedBody }));

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { telemetryEnabled: false, theme: 'dark', openMailInTabs: true },
        });
    });

    it('reads a person who has set nothing as the unset answers rather than as an absence', async () => {
        const answer = await readClientPreferences(
            session,
            answering({ status: 200, body: JSON.stringify(unsetClientPreferences) }),
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: unsetClientPreferences });
    });

    it('reports a deployment that did not answer as unavailable rather than throwing', async () => {
        const answer = await readClientPreferences(session, () => Promise.reject(new Error('nothing there')));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reports status %i as %s', async (status, reason) => {
        const answer = await readClientPreferences(session, answering({ status, body: '' }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it.each([
        ['a body that is not JSON', 'not json'],
        ['a body that is not an object', '"dark"'],
        ['a theme this build does not publish', JSON.stringify({ ...unsetClientPreferences, theme: 'sepia' })],
        ['a theme that is not a string', JSON.stringify({ ...unsetClientPreferences, theme: 3 })],
        ['a switch that is not a boolean', JSON.stringify({ ...unsetClientPreferences, openMailInTabs: 'yes' })],
        ['a preference the answer left out', JSON.stringify({ theme: 'dark', openMailInTabs: false })],
    ])('refuses %s as unreadable rather than reading a document with a hole in it', async (_, body) => {
        const answer = await readClientPreferences(session, answering({ status: 200, body }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('writeClientPreferences', () => {
    it('states the whole document as JSON on the preferences route', async () => {
        const { transport, requests } = recording({ status: 200, body: storedBody });

        await writeClientPreferences(session, transport, {
            telemetryEnabled: false,
            theme: 'dark',
            openMailInTabs: true,
        });

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/preferences');
        expect(requests[0]?.headers['Content-Type']).toBe('application/json');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            telemetryEnabled: false,
            theme: 'dark',
            openMailInTabs: true,
        });
    });

    it('answers with what is now stored', async () => {
        const answer = await writeClientPreferences(
            session,
            answering({ status: 200, body: storedBody }),
            unsetClientPreferences,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { telemetryEnabled: false, theme: 'dark', openMailInTabs: true },
        });
    });

    it('reports a deployment holding no record for the caller as unavailable', async () => {
        const answer = await writeClientPreferences(
            session,
            answering({ status: 404, body: '' }),
            unsetClientPreferences,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 404 } });
    });
});
