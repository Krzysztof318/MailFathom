// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { actPending, nothingActed, type MailboxAct, type MailboxActs } from './useMailboxActs';

const email: MailTimelineEntry = {
    id: 'message-1',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    subject: 'The quarter is closed',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: null,
    senderAddress: 'writer@nordwind.example',
    senderDisplayName: 'Writer',
    toAddresses: ['owner@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The opening of the message.',
};

function asking(act: MailboxAct, storedEmailId = email.id): MailboxActs {
    return { ...nothingActed, asked: new Map([[storedEmailId, act]]) };
}

// What retires a pending act, which is the whole of why this client polls nothing: an act writes a record and the
// account's own pass issues the mail-server command later, so what says it has landed is the deployment reporting the
// row differently — never a timer and never a second read this client asked for.
describe('actPending', () => {
    it('says nothing about a message nothing was asked of', () => {
        expect(actPending(nothingActed, email)).toBeNull();
    });

    it('says nothing about a message another one’s act was asked of', () => {
        expect(actPending(asking('archive', 'another-message'), email)).toBeNull();
    });

    it.each(['archive', 'delete', 'move'] as const)(
        'keeps saying a message is being %sd until the folder it is leaving stops listing it',
        (act) => {
            expect(actPending(asking(act), email)).toBe(act);
        },
    );

    it('stops saying a message is being flagged once the deployment reports it flagged', () => {
        expect(actPending(asking('flag'), email)).toBe('flag');
        expect(actPending(asking('flag'), { ...email, flagged: true })).toBeNull();
    });

    it('stops saying a message is being marked unread once the deployment reports it unread', () => {
        expect(actPending(asking('markUnread'), email)).toBe('markUnread');
        expect(actPending(asking('markUnread'), { ...email, unread: true })).toBeNull();
    });
});
