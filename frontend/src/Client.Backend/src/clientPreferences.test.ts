// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    longestNotificationSeconds,
    readClientPreferences,
    shortestNotificationSeconds,
    unsetClientPreferences,
    writeClientPreferences,
} from './clientPreferences';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const stored = {
    telemetryEnabled: false,
    theme: 'dark',
    openMailInTabs: true,
    markReadOnOpen: false,
    expandWholeThread: true,
    embeddedHtmlMessages: true,
    aiFiltersShown: false,
    notificationSeconds: 12,
} as const;
const storedBody = JSON.stringify(stored);

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

    it('reads the eight preferences the deployment answered', async () => {
        const answer = await readClientPreferences(session, answering({ status: 200, body: storedBody }));

        expect(answer).toStrictEqual({ outcome: 'read', value: stored });
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
        [
            'an answer from a deployment older than one of the preferences',
            JSON.stringify({ telemetryEnabled: true, theme: 'dark', openMailInTabs: false }),
        ],
        [
            'an answer from a deployment older than the thread-expansion preference',
            JSON.stringify({
                telemetryEnabled: true,
                theme: 'dark',
                openMailInTabs: false,
                markReadOnOpen: true,
            }),
        ],
        [
            'an answer from a deployment older than the message-view preference',
            JSON.stringify({
                telemetryEnabled: true,
                theme: 'dark',
                openMailInTabs: false,
                markReadOnOpen: true,
                expandWholeThread: false,
            }),
        ],
        [
            'an answer from a deployment older than the standing views in the tree',
            JSON.stringify({
                telemetryEnabled: true,
                theme: 'dark',
                openMailInTabs: false,
                markReadOnOpen: true,
                expandWholeThread: false,
                embeddedHtmlMessages: false,
            }),
        ],
        [
            'an answer from a deployment older than the notification time',
            JSON.stringify({
                telemetryEnabled: true,
                theme: 'dark',
                openMailInTabs: false,
                markReadOnOpen: true,
                expandWholeThread: false,
                embeddedHtmlMessages: false,
                aiFiltersShown: true,
            }),
        ],
    ])('refuses %s as unreadable rather than reading a document with a hole in it', async (_, body) => {
        const answer = await readClientPreferences(session, answering({ status: 200, body }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    // The bound is the deployment's and this package holds it too, for the reason every field on this surface is
    // checked: an answer outside it would reach a screen as a notification that never goes or one nobody can read.
    it.each([
        ['under the shortest', shortestNotificationSeconds - 1],
        ['over the longest', longestNotificationSeconds + 1],
        ['a fraction of a second rather than a whole one', 5.5],
        ['not a number at all', '5'],
    ])('refuses a notification time %s', async (_, notificationSeconds) => {
        const answer = await readClientPreferences(
            session,
            answering({ status: 200, body: JSON.stringify({ ...unsetClientPreferences, notificationSeconds }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([shortestNotificationSeconds, longestNotificationSeconds])(
        'reads %i seconds, which is the bound itself rather than past it',
        async (notificationSeconds) => {
            const answer = await readClientPreferences(
                session,
                answering({ status: 200, body: JSON.stringify({ ...unsetClientPreferences, notificationSeconds }) }),
            );

            expect(answer).toStrictEqual({
                outcome: 'read',
                value: { ...unsetClientPreferences, notificationSeconds },
            });
        },
    );
});

describe('writeClientPreferences', () => {
    it('states the whole document as JSON on the preferences route', async () => {
        const { transport, requests } = recording({ status: 200, body: storedBody });

        await writeClientPreferences(session, transport, stored);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/preferences');
        expect(requests[0]?.headers['Content-Type']).toBe('application/json');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual(stored);
    });

    it('answers with what is now stored', async () => {
        const answer = await writeClientPreferences(
            session,
            answering({ status: 200, body: storedBody }),
            unsetClientPreferences,
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: stored });
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
