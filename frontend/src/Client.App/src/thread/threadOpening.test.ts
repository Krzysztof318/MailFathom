// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailThreadMessage, MailThreadPage } from '@mailfathom/client-backend';
import { holdsMessage, messagesOf, openedBy } from './threadOpening';

function message(id: string, position: number, unread = false): MailThreadMessage {
    return {
        position,
        answeredId: null,
        email: {
            id,
            account: 'work',
            folder: 'INBOX',
            threadId: 'a-conversation',
            subject: 'The quarterly figures',
            receivedAt: '2026-08-31T09:41:00+00:00',
            sentAt: '2026-08-31T09:40:00+00:00',
            senderAddress: 'auditor@example.invalid',
            senderDisplayName: 'The auditor',
            toAddresses: [],
            unread,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            sizeOctets: 1_024,
            preview: 'What this one added.',
        },
    };
}

function page(messages: readonly MailThreadMessage[]): MailThreadPage {
    return {
        threadId: 'a-conversation',
        messages,
        participants: [],
        messageCount: messages.length,
        moreMessagesNotAssembled: false,
        moreParticipantsNotNamed: false,
        nextCursor: null,
        pageSize: 100,
    };
}

describe('messagesOf', () => {
    it('reads every page in the order the conversation was read in', () => {
        const pages = [page([message('one', 0), message('two', 1)]), page([message('three', 2)])];

        expect(messagesOf(pages).map((held) => held.email.id)).toStrictEqual(['one', 'two', 'three']);
    });

    it('reads a conversation nothing has been read of as holding no message', () => {
        expect(messagesOf([])).toStrictEqual([]);
    });
});

describe('holdsMessage', () => {
    it('finds a message the conversation has read', () => {
        expect(holdsMessage([message('one', 0), message('two', 1)], 'two')).toBe(true);
    });

    it('does not find one it has not read yet', () => {
        expect(holdsMessage([message('one', 0)], 'two')).toBe(false);
    });
});

describe('openedBy', () => {
    it('opens at the message somebody arrived at, which is the context they came for', () => {
        const messages = [message('one', 0, true), message('two', 1), message('three', 2)];

        expect(openedBy(messages, 'two')).toStrictEqual(['two']);
    });

    it('opens at what has not been read where nobody named a message', () => {
        const messages = [message('one', 0), message('two', 1, true), message('three', 2, true)];

        expect(openedBy(messages, null)).toStrictEqual(['two', 'three']);
    });

    it('opens at the last word of a conversation everybody has read', () => {
        const messages = [message('one', 0), message('two', 1), message('three', 2)];

        expect(openedBy(messages, null)).toStrictEqual(['three']);
    });

    it('opens at what has not been read where the message named is not among those read', () => {
        const messages = [message('one', 0), message('two', 1, true)];

        expect(openedBy(messages, 'somewhere-else')).toStrictEqual(['two']);
    });

    it('opens the most recent of what has not been read rather than all of it, because each open message is a read', () => {
        const messages = [
            message('one', 0, true),
            message('two', 1, true),
            message('three', 2, true),
            message('four', 3, true),
            message('five', 4, true),
        ];

        expect(openedBy(messages, null)).toStrictEqual(['three', 'four', 'five']);
    });

    it('opens nothing in a conversation holding no message anybody may see', () => {
        expect(openedBy([], 'two')).toStrictEqual([]);
    });
});
