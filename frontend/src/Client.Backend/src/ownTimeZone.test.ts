// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { changeOwnTimeZone, longestTimeZoneAnswer, readOwnTimeZone, reportedTimeZone } from './ownTimeZone';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const chosenBody = JSON.stringify({ timeZone: 'Europe/Warsaw', isDefault: false });

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

describe('readOwnTimeZone', () => {
    it('asks for the zone route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: chosenBody });

        await readOwnTimeZone(session, transport);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/time-zone');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
        expect(requests[0]?.longestAnswer).toBe(longestTimeZoneAnswer);
    });

    it('reads the zone this deployment records the person’s days in', async () => {
        const answer = await readOwnTimeZone(session, answering({ status: 200, body: chosenBody }));

        expect(answer).toStrictEqual({ outcome: 'read', value: { timeZone: 'Europe/Warsaw', isDefault: false } });
    });

    // Whether the value is still the one an unstated record falls to is the deployment's answer rather than something
    // to infer by comparing identifiers, because somebody who chose the coordinated zone deliberately reads the same.
    it('reads a record nobody has stated a zone in as one still holding the default', async () => {
        const answer = await readOwnTimeZone(
            session,
            answering({ status: 200, body: JSON.stringify({ timeZone: 'UTC', isDefault: true }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: { timeZone: 'UTC', isDefault: true } });
    });

    it.each([
        ['a body that is not an answer at all', 'not json'],
        ['an answer naming no zone', JSON.stringify({ isDefault: true })],
        ['an answer that says nothing about the default', JSON.stringify({ timeZone: 'UTC' })],
        ['a zone answered as something other than an identifier', JSON.stringify({ timeZone: 7, isDefault: true })],
    ])('refuses %s', async (_, body) => {
        const answer = await readOwnTimeZone(session, answering({ status: 200, body }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a credential this deployment no longer accepts', async () => {
        const answer = await readOwnTimeZone(session, answering({ status: 401, body: '' }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });

    it('reports a deployment it could not reach at all', async () => {
        const answer = await readOwnTimeZone(session, () => Promise.reject(new Error('the connection was refused')));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('changeOwnTimeZone', () => {
    it('sends the identifier in a body rather than in the request line', async () => {
        const { transport, requests } = recording({ status: 200, body: chosenBody });

        await changeOwnTimeZone(session, transport, 'Europe/Warsaw');

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/time-zone');
        expect(JSON.parse(requests[0]?.body ?? '{}')).toStrictEqual({ timeZone: 'Europe/Warsaw' });
    });

    // The zone comes back from the answer rather than from what was sent, because what every date is drawn in is what
    // the deployment recorded.
    it('answers with the zone the deployment recorded', async () => {
        const change = await changeOwnTimeZone(session, answering({ status: 200, body: chosenBody }), 'Europe/Warsaw');

        expect(change).toStrictEqual({ outcome: 'recorded', timeZone: 'Europe/Warsaw' });
    });

    // A zone this deployment does not know is the one outcome a person acts on, by choosing another — which is why it
    // is separated from the four failures rather than being one of them.
    it('reports a zone this deployment does not know as one to choose again rather than as a failure', async () => {
        const change = await changeOwnTimeZone(session, answering({ status: 400, body: '' }), 'Europe/Warszawa');

        expect(change).toStrictEqual({ outcome: 'notAcceptable' });
    });

    it('reports a deployment holding no record for the caller', async () => {
        const change = await changeOwnTimeZone(session, answering({ status: 404, body: '' }), 'Europe/Warsaw');

        expect(change).toStrictEqual({
            outcome: 'failed',
            failure: { reason: 'unavailable', status: 404 },
        });
    });

    it('reports a deployment it could not reach at all', async () => {
        const change = await changeOwnTimeZone(
            session,
            () => Promise.reject(new Error('the connection was refused')),
            'Europe/Warsaw',
        );

        expect(change).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

// What the runtime reports is stated rather than taken from the machine the suite is running on, which is the whole
// point of these three: an assertion written as `expect(reportedTimeZone()).toBe(Intl.…().resolvedOptions().timeZone)`
// passes for a function that answers anything at all, and one written against the developer's own zone passes there
// and fails on a server. This package declares no Node types by design, so the zone is stated by standing in for what
// `Intl` resolves rather than by assigning `TZ`; the real options are read first so the stub is a whole one.
function reporting(timeZone: string): void {
    const resolved = new Intl.DateTimeFormat().resolvedOptions();

    vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockReturnValue({ ...resolved, timeZone });
}

afterEach(() => {
    vi.restoreAllMocks();
});

describe('reportedTimeZone', () => {
    it('answers with the identifier this runtime reports it is in', () => {
        reporting('Asia/Tokyo');

        expect(reportedTimeZone()).toBe('Asia/Tokyo');
    });

    // The case a developer's own machine cannot show and a server is always in: `UTC` is what a machine genuinely set
    // to the coordinated zone reports, and it is passed on like any other name. Nothing here reads it as an engine
    // with no zone database, because the two are indistinguishable from here and the deployment refuses what it does
    // not carry anyway — and recording it changes nothing, an unstated record already reading as `UTC`.
    it('answers with the coordinated zone where that is what the runtime reports', () => {
        reporting('UTC');

        expect(reportedTimeZone()).toBe('UTC');
    });

    it('answers with nothing where the runtime names no zone at all', () => {
        reporting('');

        expect(reportedTimeZone()).toBeNull();
    });
});
