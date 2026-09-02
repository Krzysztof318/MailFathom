// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readMailTimeline, timelineQueryString, type MailTimelineQuery } from './mailTimeline';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const leadingPage: MailTimelineQuery = {
    account: null,
    folder: null,
    includeJunk: false,
    unread: null,
    flagged: null,
    hasAttachments: null,
    order: 'newestFirst',
    direction: 'forward',
    pageSize: 50,
    cursor: null,
};

const message = {
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
};

function bodyOf(emails: readonly unknown[], cursors: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({ emails, nextCursor: null, previousCursor: null, pageSize: 50, ...cursors });
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

describe('timelineQueryString', () => {
    it('states the sort, the order, the direction, and the page size even where the route would default them', () => {
        expect(timelineQueryString(leadingPage)).toBe(
            '?sort=receivedAt&order=newestFirst&direction=forward&pageSize=50',
        );
    });

    it('names the folder a role scope stands for', () => {
        expect(timelineQueryString({ ...leadingPage, folder: 'role:Inbox' })).toContain('folder=role%3AInbox');
    });

    it('escapes an account name a mail server chose', () => {
        expect(timelineQueryString({ ...leadingPage, account: 'work & home' })).toContain('account=work%20%26%20home');
    });

    it('asks for the junk folder only where the list includes it', () => {
        expect(timelineQueryString(leadingPage)).not.toContain('includeJunk');
        expect(timelineQueryString({ ...leadingPage, includeJunk: true })).toContain('includeJunk=true');
    });

    it.each([
        [{ unread: true }, 'unread=true'],
        [{ unread: false }, 'unread=false'],
        [{ flagged: true }, 'flagged=true'],
        [{ hasAttachments: false }, 'hasAttachments=false'],
    ])('states %o as %s', (filter, expected) => {
        expect(timelineQueryString({ ...leadingPage, ...filter })).toContain(expected);
    });

    it('leaves out a filter that keeps both answers', () => {
        const asked = timelineQueryString(leadingPage);

        expect(asked).not.toContain('unread');
        expect(asked).not.toContain('flagged');
        expect(asked).not.toContain('hasAttachments');
    });

    it('escapes the cursor a previous page answered with', () => {
        expect(timelineQueryString({ ...leadingPage, cursor: 'a+b/c=' })).toContain('cursor=a%2Bb%2Fc%3D');
    });
});

describe('readMailTimeline', () => {
    it('asks for the timeline route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf([message]) });

        await readMailTimeline(session, transport, leadingPage);

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/emails?sort=receivedAt&order=newestFirst&direction=forward&pageSize=50',
        );
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('bounds the answer it will read below the transport backstop', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf([]) });

        await readMailTimeline(session, transport, leadingPage);

        expect(requests[0]?.longestAnswer).toBe(262_144);
    });

    it('reads a page and the two cursors the pages either side of it are asked with', async () => {
        const transport = answering({
            status: 200,
            body: bodyOf([message], { nextCursor: 'after', previousCursor: 'before' }),
        });

        const result = await readMailTimeline(session, transport, leadingPage);

        expect(result).toStrictEqual({
            outcome: 'read',
            value: { emails: [message], nextCursor: 'after', previousCursor: 'before', pageSize: 50 },
        });
    });

    it('reads the end of a list as no cursor rather than as a hint to ask again', async () => {
        const transport = answering({ status: 200, body: bodyOf([]) });

        const result = await readMailTimeline(session, transport, leadingPage);

        expect(result.outcome === 'read' && result.value.nextCursor).toBeNull();
    });

    it('reads a message this deployment has stored but not extracted as one with no preview', async () => {
        const transport = answering({ status: 200, body: bodyOf([{ ...message, preview: null }]) });

        const result = await readMailTimeline(session, transport, leadingPage);

        expect(result.outcome === 'read' && result.value.emails[0]?.preview).toBeNull();
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [400, 'unavailable'],
        [500, 'unavailable'],
    ])('reports %i as %s', async (status, reason) => {
        const result = await readMailTimeline(session, answering({ status, body: '' }), leadingPage);

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment that did not answer at all as unavailable', async () => {
        const result = await readMailTimeline(
            session,
            () => Promise.reject(new Error('the name does not resolve')),
            leadingPage,
        );

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('refuses a page carrying more rows than the request admits', async () => {
        const crowded = bodyOf([message, message, message]);

        const result = await readMailTimeline(session, answering({ status: 200, body: crowded }), {
            ...leadingPage,
            pageSize: 2,
        });

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['a body that is not JSON', 'not json'],
        ['a body that is not an object', JSON.stringify([])],
        ['rows that are not an array', JSON.stringify({ emails: {}, pageSize: 50 })],
        ['a page size that is not a number', bodyOf([], { pageSize: 'fifty' })],
        ['a page size below one', bodyOf([], { pageSize: 0 })],
        ['a cursor that is not text', bodyOf([], { nextCursor: 7 })],
        ['a cursor with nothing in it', bodyOf([], { previousCursor: '' })],
        ['a row that is not an object', bodyOf(['message'])],
        ['a row with no identity', bodyOf([{ ...message, id: '' }])],
        ['a row naming no account', bodyOf([{ ...message, account: null }])],
        ['a row naming no folder', bodyOf([{ ...message, folder: 7 }])],
        ['a conversation that is not an identity', bodyOf([{ ...message, threadId: 7 }])],
        ['a subject that is not text', bodyOf([{ ...message, subject: 7 }])],
        ['a received time that is not text', bodyOf([{ ...message, receivedAt: 1_756_632_060 }])],
        ['recipients that are not an array', bodyOf([{ ...message, toAddresses: 'owner@example.invalid' }])],
        ['a recipient that is not text', bodyOf([{ ...message, toAddresses: [null] }])],
        ['an unread state that is not a boolean', bodyOf([{ ...message, unread: 'yes' }])],
        ['a flagged state that is not a boolean', bodyOf([{ ...message, flagged: null }])],
        ['an attachment count that is not whole', bodyOf([{ ...message, attachmentCount: 1.5 }])],
        ['a negative size', bodyOf([{ ...message, sizeOctets: -1 }])],
    ])('refuses %s rather than reading a page with a hole in it', async (_, body) => {
        const result = await readMailTimeline(session, answering({ status: 200, body }), leadingPage);

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a row carrying more recipients than a message has', async () => {
        const crowd = Array.from({ length: 257 }, (_, at) => `person-${String(at)}@example.invalid`);

        const result = await readMailTimeline(
            session,
            answering({ status: 200, body: bodyOf([{ ...message, toAddresses: crowd }]) }),
            leadingPage,
        );

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a message nothing has placed in a conversation as one with no thread', async () => {
        const transport = answering({ status: 200, body: bodyOf([{ ...message, threadId: undefined }]) });

        const result = await readMailTimeline(session, transport, leadingPage);

        expect(result.outcome === 'read' && result.value.emails[0]?.threadId).toBeNull();
    });
});
