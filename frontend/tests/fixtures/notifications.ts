// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What happened to the person while nobody was looking at their screen, and what each of the two ways of marking one
// read answers with.
//
// The three targets are all stated, because which one a producer chose is what a row is drawn by: a notification
// leading to a message, one leading to a screen, and one leading nowhere are three different rows rather than one row
// with a field that is sometimes absent.

import { newsletterId } from './messages';

/** The identity the unread notification below is addressed by. */
export const notificationId = '00000000-0000-4000-8000-0000000000f0';

/** One page of the centre, newest first, holding one row of each kind of target and both read states. */
export const notificationPage = {
    notifications: [
        {
            id: notificationId,
            kind: 'Mail',
            title: 'Nordwind wrote about the racking quote',
            body: 'Booked for the ninth. The crew will need the yard for a morning.',
            source: 'Work',
            target: { kind: 'Message', messageId: newsletterId },
            occurredAt: '2026-08-31T08:03:00+00:00',
            read: false,
        },
        {
            id: '00000000-0000-4000-8000-0000000000f1',
            kind: 'System',
            title: 'The Club mailbox is refusing its credential',
            body: 'Nothing has been read from it since Sunday evening.',
            source: 'Club',
            target: { kind: 'Screen', screen: 'Settings' },
            occurredAt: '2026-08-30T18:12:00+00:00',
            read: false,
        },
        {
            id: '00000000-0000-4000-8000-0000000000f2',
            kind: 'Task',
            title: 'Four messages were filed into Receipts',
            body: '',
            source: null,
            target: { kind: 'Nothing' },
            occurredAt: '2026-08-29T11:00:00+00:00',
            read: true,
        },
    ],
    nextCursor: null,
};

/** What a centre with nothing in it answers with, which a panel says something of its own about. */
export const emptyNotificationPage = { notifications: [], nextCursor: null };

/** What the count route answers on its own, which is the whole of what the bell draws. */
export const unreadNotificationCount = { unreadCount: 2 };

/** What marking {@link notificationId} read answers with: the row's new state, and what it leaves on the bell. */
export const notificationMarkedRead = { id: notificationId, read: true, unreadCount: 1 };

/** What marking the whole centre read answers with, which a client redraws both the panel and the bell from. */
export const everyNotificationMarkedRead = { markedRead: 2, unreadCount: 0 };
