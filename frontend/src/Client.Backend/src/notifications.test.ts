// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    longestNotificationPage,
    markAllNotificationsRead,
    readNotifications,
    readUnreadNotificationCount,
    setNotificationRead,
} from './notifications';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

// The transport is the network boundary and the whole of what a test here fakes. No route in this module reads a
// header off an answer, so each helper supplies the empty set.
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

const arrived = {
    id: 'n-1',
    kind: 'Mail',
    title: 'Ada Lovelace wrote',
    body: 'About the engine',
    source: 'Inbox',
    target: { kind: 'Message', messageId: 'm-9' },
    occurredAt: '2026-09-04T09:00:00+00:00',
    read: false,
};

function page(entries: readonly unknown[], nextCursor: string | null = null): string {
    return JSON.stringify({ notifications: entries, nextCursor });
}

describe('readNotifications', () => {
    it('asks for the notifications route with the window the screen actually draws', async () => {
        const { transport, requests } = recording({ status: 200, body: page([]) });

        await readNotifications(session, transport, 25);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/notifications?pageSize=25');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('asks within the ceiling this surface serves rather than for what a caller named above it', async () => {
        const { transport, requests } = recording({ status: 200, body: page([]) });

        await readNotifications(session, transport, 5_000);

        expect(requests[0]?.path).toBe(
            `https://mail.example.invalid/api/client/notifications?pageSize=${String(longestNotificationPage)}`,
        );
    });

    it('reads a page as the rows a screen draws and the boundary the next one is asked with', async () => {
        const answer = await readNotifications(
            session,
            answering({ status: 200, body: page([arrived], 'after-1') }),
            25,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: {
                nextCursor: 'after-1',
                notifications: [
                    {
                        id: 'n-1',
                        kind: 'Mail',
                        title: 'Ada Lovelace wrote',
                        body: 'About the engine',
                        source: 'Inbox',
                        target: { kind: 'Message', storedEmailId: 'm-9' },
                        occurredAt: '2026-09-04T09:00:00+00:00',
                        read: false,
                    },
                ],
            },
        });
    });

    it('reads a notification carrying no source of its own as one whose kind is the whole of the line', async () => {
        const { source, ...withoutSource } = arrived;
        const answer = await readNotifications(session, answering({ status: 200, body: page([withoutSource]) }), 25);

        expect(source).toBe('Inbox');
        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { nextCursor: null, notifications: [expect.objectContaining({ source: null })] },
        });
    });

    it.each([
        ['Nothing', { kind: 'Nothing' }, { kind: 'Nothing' }],
        ['Message', { kind: 'Message', messageId: 'm-9' }, { kind: 'Message', storedEmailId: 'm-9' }],
        ['Screen', { kind: 'Screen', screen: 'Settings' }, { kind: 'Screen', screen: 'Settings' }],
    ])('reads a %s target as the shape a reader switches on', async (_named, sent, expected) => {
        const answer = await readNotifications(
            session,
            answering({ status: 200, body: page([{ ...arrived, target: sent }]) }),
            25,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { nextCursor: null, notifications: [expect.objectContaining({ target: expected })] },
        });
    });

    it('refuses a page carrying more than was asked for rather than drawing it', async () => {
        const asked = 2;
        const answer = await readNotifications(
            session,
            answering({ status: 200, body: page([arrived, arrived, arrived]) }),
            asked,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['a kind this client does not have', { ...arrived, kind: 'Weather' }],
        ['a target shape this client does not have', { ...arrived, target: { kind: 'Planet', planet: 'Mars' } }],
        ['a message target naming no message', { ...arrived, target: { kind: 'Message', messageId: '' } }],
        [
            'a screen target naming a screen this client does not have',
            {
                ...arrived,
                target: { kind: 'Screen', screen: 'Warehouse' },
            },
        ],
        ['no identifier', { ...arrived, id: '' }],
        ['a read state that is not a state', { ...arrived, read: 'yes' }],
        ['a source that is not a line', { ...arrived, source: 7 }],
    ])('reports a row carrying %s as unreadable rather than rendering it', async (_named, entry) => {
        const answer = await readNotifications(session, answering({ status: 200, body: page([entry]) }), 25);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a body that is not a page as unreadable', async () => {
        const answer = await readNotifications(session, answering({ status: 200, body: 'not json' }), 25);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a deployment that did not answer as unavailable rather than throwing', async () => {
        const answer = await readNotifications(session, () => Promise.reject(new Error('nothing there')), 25);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
    ])('reports %i as %s', async (status, reason) => {
        const answer = await readNotifications(session, answering({ status, body: '' }), 25);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });
});

describe('readUnreadNotificationCount', () => {
    it('asks the count route on its own rather than counting a page out', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ unreadCount: 3 }) });

        await readUnreadNotificationCount(session, transport);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/notifications/unread-count');
    });

    it('reads the count the deployment answered', async () => {
        const answer = await readUnreadNotificationCount(
            session,
            answering({ status: 200, body: JSON.stringify({ unreadCount: 12 }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: 12 });
    });

    it.each([
        ['a fraction', 1.5],
        ['a negative', -1],
        ['something that is not a number at all', 'three'],
    ])('reports %s as unreadable rather than drawing it on the bell', async (_named, unreadCount) => {
        const answer = await readUnreadNotificationCount(
            session,
            answering({ status: 200, body: JSON.stringify({ unreadCount }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a deployment that did not answer as unavailable rather than throwing', async () => {
        const answer = await readUnreadNotificationCount(session, () => Promise.reject(new Error('nothing there')));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('setNotificationRead', () => {
    it('names the notification in the route and the state it is to stand in, in the body', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ id: 'n 1', read: true, unreadCount: 2 }),
        });

        await setNotificationRead(session, transport, 'n 1', true);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/notifications/n%201/read-state');
        expect(requests[0]?.body).toBe(JSON.stringify({ read: true }));
    });

    it('reads back the state the row now stands in and what it leaves on the bell, so one exchange settles both', async () => {
        const answer = await setNotificationRead(
            session,
            answering({ status: 200, body: JSON.stringify({ id: 'n-1', read: false, unreadCount: 4 }) }),
            'n-1',
            false,
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: { id: 'n-1', read: false, unreadCount: 4 } });
    });

    it('reports an answer carrying no count as unreadable rather than leaving the badge to guess', async () => {
        const answer = await setNotificationRead(
            session,
            answering({ status: 200, body: JSON.stringify({ id: 'n-1', read: true }) }),
            'n-1',
            true,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a deployment that did not answer as unavailable rather than throwing', async () => {
        const answer = await setNotificationRead(
            session,
            () => Promise.reject(new Error('nothing there')),
            'n-1',
            true,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('markAllNotificationsRead', () => {
    it('marks the whole centre read in one request rather than one per row', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ markedRead: 7, unreadCount: 0 }),
        });

        await markAllNotificationsRead(session, transport);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/notifications/read');
    });

    it('reads how many it marked and what stands unread afterwards', async () => {
        const answer = await markAllNotificationsRead(
            session,
            answering({ status: 200, body: JSON.stringify({ markedRead: 7, unreadCount: 0 }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: { markedRead: 7, unreadCount: 0 } });
    });

    it('reports an answer missing either count as unreadable', async () => {
        const answer = await markAllNotificationsRead(
            session,
            answering({ status: 200, body: JSON.stringify({ markedRead: 7 }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a deployment that did not answer as unavailable rather than throwing', async () => {
        const answer = await markAllNotificationsRead(session, () => Promise.reject(new Error('nothing there')));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
