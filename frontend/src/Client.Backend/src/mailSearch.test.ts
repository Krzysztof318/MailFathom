// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readMailSearch, searchQueryString, type MailSearchQuery } from './mailSearch';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const bestRanked: MailSearchQuery = {
    text: 'quarterly figures',
    account: null,
    folder: null,
    includeJunk: false,
    sender: null,
    recipient: null,
    unread: null,
    flagged: null,
    hasAttachments: null,
    receivedOnOrAfter: null,
    receivedBefore: null,
    pageSize: 50,
    cursor: null,
};

const result = {
    id: '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    subject: 'The quarterly figures',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: '2026-08-31T09:40:00+00:00',
    senderAddress: 'auditor@example.invalid',
    senderDisplayName: 'The auditor',
    toAddresses: ['owner@example.invalid'],
    unread: true,
    flagged: false,
    answered: false,
    hasAttachments: true,
    attachmentCount: 2,
    sizeOctets: 84_213,
    preview: 'The figures you asked for are attached.',
    snippets: ['The **quarterly figures** you asked for'],
    matchedBy: 'BothRankings',
};

function bodyOf(results: readonly unknown[], page: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        results,
        nextCursor: null,
        pageSize: 50,
        retrievalMode: 'Hybrid',
        semanticSearch: 'Available',
        includedJunkMail: false,
        ...page,
    });
}

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

describe('searchQueryString', () => {
    it('asks for the text and the page size, and for nothing the search was not narrowed by', () => {
        expect(searchQueryString(bestRanked)).toBe('?query=quarterly%20figures&pageSize=50');
    });

    it('names the folder a role stands for', () => {
        expect(searchQueryString({ ...bestRanked, folder: 'role:Inbox' })).toContain('folder=role%3AInbox');
    });

    it('carries the received range as the two instants that bound it', () => {
        const asked = searchQueryString({
            ...bestRanked,
            receivedOnOrAfter: '2026-08-01T00:00:00.000Z',
            receivedBefore: '2026-09-01T00:00:00.000Z',
        });

        expect(asked).toContain('receivedOnOrAfter=2026-08-01T00%3A00%3A00.000Z');
        expect(asked).toContain('receivedBefore=2026-09-01T00%3A00%3A00.000Z');
    });

    it('asks for junk only where the search said so', () => {
        expect(searchQueryString(bestRanked)).not.toContain('includeJunk');
        expect(searchQueryString({ ...bestRanked, includeJunk: true })).toContain('includeJunk=true');
    });

    it.each([
        [{ unread: true }, 'unread=true'],
        [{ unread: false }, 'unread=false'],
        [{ flagged: true }, 'flagged=true'],
        [{ hasAttachments: false }, 'hasAttachments=false'],
    ])('states %o as %s', (narrowing, asked) => {
        expect(searchQueryString({ ...bestRanked, ...narrowing })).toContain(asked);
    });

    it('escapes text a person typed rather than letting it reach the query string as separators', () => {
        expect(searchQueryString({ ...bestRanked, text: 'a&b=c' })).toContain('query=a%26b%3Dc');
    });
});

describe('readMailSearch', () => {
    it('reads a page of results with what says why each of them is there', async () => {
        const answer = await readMailSearch(session, answering({ status: 200, body: bodyOf([result]) }), bestRanked);

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: {
                results: [
                    expect.objectContaining({
                        id: result.id,
                        snippets: ['The **quarterly figures** you asked for'],
                        matchedBy: 'BothRankings',
                    }) as unknown,
                ],
                nextCursor: null,
                pageSize: 50,
                retrievalMode: 'Hybrid',
                semanticSearch: 'Available',
                includedJunkMail: false,
            },
        });
    });

    it('reads a result that matched by meaning alone as one with no extract of it to show', async () => {
        const meaning = { ...result, snippets: [], matchedBy: 'SemanticRanking' };
        const answer = await readMailSearch(session, answering({ status: 200, body: bodyOf([meaning]) }), bestRanked);

        expect(answer).toStrictEqual(
            expect.objectContaining({
                outcome: 'read',
                value: expect.objectContaining({
                    results: [expect.objectContaining({ snippets: [], matchedBy: 'SemanticRanking' }) as unknown],
                }) as unknown,
            }),
        );
    });

    it('reads a page ranked by words alone on a deployment that has activated no embedding profile', async () => {
        const lexical = bodyOf([result], { retrievalMode: 'Lexical', semanticSearch: 'Inactive' });
        const answer = await readMailSearch(session, answering({ status: 200, body: lexical }), bestRanked);

        expect(answer.outcome === 'read' ? answer.value.semanticSearch : null).toBe('Inactive');
    });

    it('reads a search that matched nothing as a page holding nothing rather than as a failure', async () => {
        const answer = await readMailSearch(session, answering({ status: 200, body: bodyOf([]) }), bestRanked);

        expect(answer.outcome === 'read' ? answer.value.results : null).toStrictEqual([]);
    });

    it('presents the credential and asks the route the search is served at', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf([]) });

        await readMailSearch(session, transport, bestRanked);

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/emails/search?query=quarterly%20figures&pageSize=50',
        );
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('reads a deployment that did not answer at all as unavailable', async () => {
        const answer = await readMailSearch(
            session,
            () => Promise.reject(new Error('the name does not resolve')),
            bestRanked,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [400, 'unreadable'],
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reads %i as %s', async (status, reason) => {
        const answer = await readMailSearch(session, answering({ status, body: '' }), bestRanked);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it.each([
        ['a body that is not JSON', 'not json'],
        ['a page holding no results at all', JSON.stringify({ nextCursor: null, pageSize: 50 })],
        ['a page ranked in a way this client cannot draw', bodyOf([], { retrievalMode: 'Vector' })],
        ['a page naming a semantic state this client cannot draw', bodyOf([], { semanticSearch: 'Broken' })],
        ['a page that does not say whether junk took part', bodyOf([], { includedJunkMail: null })],
        ['a page continued by an empty cursor', bodyOf([], { nextCursor: '' })],
        ['a result found by a ranking this client cannot name', bodyOf([{ ...result, matchedBy: 'Guesswork' }])],
        ['a result whose extracts are not a list', bodyOf([{ ...result, snippets: 'one' }])],
        ['a result whose extracts hold something that is not text', bodyOf([{ ...result, snippets: [7] }])],
        ['a result with no identity', bodyOf([{ ...result, id: null }])],
    ])('refuses %s rather than drawing it', async (_, body) => {
        const answer = await readMailSearch(session, answering({ status: 200, body }), bestRanked);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a page holding more results than the search asked for', async () => {
        const body = bodyOf([result, { ...result, id: 'a5b1c2d3-4e5f-4061-8273-849506172839' }], { pageSize: 1 });
        const answer = await readMailSearch(session, answering({ status: 200, body }), { ...bestRanked, pageSize: 1 });

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
