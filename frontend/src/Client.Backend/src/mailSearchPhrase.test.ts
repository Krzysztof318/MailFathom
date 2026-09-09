// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { calendarDayOf, readMailSearchPhrase, readsMailSearchPhrases } from './mailSearchPhrase';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const filters = {
    sender: null,
    recipient: null,
    receivedFrom: null,
    receivedTo: null,
    unread: false,
    flagged: false,
    hasAttachments: false,
};

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

function readingOf(reading: Readonly<Record<string, unknown>>): string {
    return JSON.stringify({ read: true, filters, criteria: [], unaccounted: null, ...reading });
}

describe('readsMailSearchPhrases', () => {
    it('reads whether this deployment turns a sentence into filters', async () => {
        const answer = await readsMailSearchPhrases(
            session,
            answering({ status: 200, body: JSON.stringify({ readsPhrases: true }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: true });
    });

    it('asks the deployment at the route the service serves it at', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ readsPhrases: false }) });

        await readsMailSearchPhrases(session, transport);

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toContain('/emails/search/phrasing');
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reports a %d as %s', async (status, reason) => {
        const answer = await readsMailSearchPhrases(session, answering({ status, body: '' }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('refuses an answer that says nothing about whether a sentence is read', async () => {
        const answer = await readsMailSearchPhrases(session, answering({ status: 200, body: '{}' }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    // A present value is not an answer. What this screen promises somebody turns on the field being a boolean, so a
    // string or a number is refused exactly as an absent one is rather than being read for its truthiness.
    it.each([['yes'], [1], [null], [{}]])('refuses %o as an answer about whether a sentence is read', async (reads) => {
        const answer = await readsMailSearchPhrases(
            session,
            answering({ status: 200, body: JSON.stringify({ readsPhrases: reads }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('readMailSearchPhrase', () => {
    it('reads the constraints, the criteria, and the part nothing was made of', async () => {
        const answer = await readMailSearchPhrase(
            session,
            answering({
                status: 200,
                body: readingOf({
                    filters: { ...filters, sender: 'sales@example.invalid', receivedFrom: '2026-08-01', unread: true },
                    criteria: ['racking quotation'],
                    unaccounted: 'urgent',
                }),
            }),
            'unread mail from sales about racking since August, urgent',
            '2026-09-09',
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: {
                read: true,
                filters: { ...filters, sender: 'sales@example.invalid', receivedFrom: '2026-08-01', unread: true },
                criteria: ['racking quotation'],
                unaccounted: 'urgent',
            },
        });
    });

    // The sentence is the most revealing value this client sends, and a query string is the part of a request that
    // reaches an access log by default — so it travels in a body, which is the whole reason this is a POST.
    it('sends the sentence and the reader’s own day in a body rather than in the request line', async () => {
        const { transport, requests } = recording({ status: 200, body: readingOf({}) });

        await readMailSearchPhrase(session, transport, 'mail from last week', '2026-09-09');

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).not.toContain('last%20week');
        expect(JSON.parse(requests[0]?.body ?? '{}')).toStrictEqual({
            phrase: 'mail from last week',
            askedOn: '2026-09-09',
        });
    });

    it('reads a deployment that read nothing as the plain word search rather than as a failure', async () => {
        const answer = await readMailSearchPhrase(
            session,
            answering({ status: 200, body: readingOf({ read: false }) }),
            'unread mail about the invoice',
            '2026-09-09',
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { read: false, filters, criteria: [], unaccounted: null },
        });
    });

    // Half a reading drawn as a complete one is the silent reinterpretation this screen exists to prevent, so an answer
    // past any bound is refused whole rather than trimmed.
    it.each([
        ['more criteria than a sentence is read into', { criteria: ['a', 'b', 'c', 'd', 'e'] }],
        ['a criterion longer than one may be', { criteria: ['a'.repeat(97)] }],
        ['an unaccounted part longer than a sentence', { unaccounted: 'a'.repeat(257) }],
        ['a criterion that is not a phrase at all', { criteria: [7] }],
        ['a day no calendar control could hold', { filters: { ...filters, receivedFrom: '15 August' } }],
        ['an address longer than one may be', { filters: { ...filters, sender: `${'a'.repeat(320)}@x` } }],
        ['a state answered as something other than a state', { filters: { ...filters, unread: 'yes' } }],
    ])('refuses an answer carrying %s', async (_, answered) => {
        const answer = await readMailSearchPhrase(
            session,
            answering({ status: 200, body: readingOf(answered) }),
            'unread mail about the invoice',
            '2026-09-09',
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    // A ceiling the operator set is neither a defect in this client nor something a reader repairs, so the screen falls
    // back to the word search exactly as it does on a deployment that reads no sentence.
    it('reports a deployment that has spent what it allows a provider as the capability being unavailable', async () => {
        const answer = await readMailSearchPhrase(
            session,
            answering({ status: 429, body: '' }),
            'unread mail about the invoice',
            '2026-09-09',
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 429 } });
    });

    it('reports a refused sentence as one this deployment could not read', async () => {
        const answer = await readMailSearchPhrase(session, answering({ status: 400, body: '' }), '   ', '2026-09-09');

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 400 } });
    });

    it('reports a deployment it could not reach at all', async () => {
        const answer = await readMailSearchPhrase(
            session,
            () => Promise.reject(new Error('the connection was refused')),
            'unread mail about the invoice',
            '2026-09-09',
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('calendarDayOf', () => {
    // Composed from the local parts rather than from an ISO instant: somebody searching late in the evening east of
    // Greenwich would otherwise have "today" resolved to tomorrow, and every relative expression with it.
    it('writes the day the reader is standing on rather than the day in UTC', () => {
        const lateEvening = new Date(2026, 8, 9, 23, 30);

        expect(calendarDayOf(lateEvening)).toBe('2026-09-09');
    });

    it('pads a month and a day the way a calendar control writes them', () => {
        expect(calendarDayOf(new Date(2026, 0, 3))).toBe('2026-01-03');
    });
});
