// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailThreadMessage, MailThreadPage } from '@mailfathom/client-backend';
import { arrivalMark, arrivesAt, holdsMessage, messagesOf } from './threadOpening';

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

describe('arrivesAt', () => {
    it('arrives at the message somebody was sent to, which is the context they came for', () => {
        const messages = [message('one', 0, true), message('two', 1), message('three', 2)];

        expect(arrivesAt(messages, 'two')).toStrictEqual({ storedEmailId: 'two', amongOthers: true });
    });

    it('arrives at the latest of a conversation nobody named a message in', () => {
        const messages = [message('one', 0), message('two', 1, true), message('three', 2)];

        expect(arrivesAt(messages, null)).toStrictEqual({ storedEmailId: 'three', amongOthers: false });
    });

    it('arrives at the latest where the message named is not among those read', () => {
        const messages = [message('one', 0), message('two', 1, true)];

        expect(arrivesAt(messages, 'somewhere-else')).toStrictEqual({ storedEmailId: 'two', amongOthers: false });
    });

    it('arrives among others where the message named is the conversation only up to its latest', () => {
        const messages = [message('one', 0), message('two', 1), message('three', 2)];

        expect(arrivesAt(messages, 'one')).toStrictEqual({ storedEmailId: 'one', amongOthers: true });
    });

    it('arrives nowhere in a conversation holding no message anybody may see', () => {
        expect(arrivesAt([], 'two')).toBeNull();
    });
});

describe('arrivalMark', () => {
    const amongOthers = { storedEmailId: 'two', amongOthers: true };
    const alone = { storedEmailId: 'three', amongOthers: false };

    it('marks the message somebody opened from the list as the one they opened', () => {
        expect(arrivalMark({ threadId: 'a-conversation', openAt: 'two' }, amongOthers, false)).toBe('list');
    });

    it('marks nothing in a conversation opened on its own subject, where nobody was sent to a message', () => {
        expect(arrivalMark({ threadId: 'a-conversation', openAt: null }, alone, false)).toBeNull();
    });

    it('marks nothing while the conversation has not decided where it arrives', () => {
        expect(arrivalMark({ threadId: 'a-conversation', openAt: 'two' }, null, false)).toBeNull();
    });

    it('marks nothing where the conversation stood the reader in front of that message alone', () => {
        expect(arrivalMark({ threadId: 'a-conversation', openAt: 'three' }, alone, false)).toBeNull();
    });

    it('marks a message landed on from a search result as one somebody was brought to', () => {
        expect(arrivalMark({ threadId: 'a-conversation', openAt: 'two', fromResult: true }, amongOthers, false)).toBe(
            'result',
        );
    });

    it('marks a landing that has settled as nothing at all, which is the ordinary open message', () => {
        expect(
            arrivalMark({ threadId: 'a-conversation', openAt: 'two', fromResult: true }, amongOthers, true),
        ).toBeNull();
    });

    it('marks a landing arrived at alone, it saying what the client just did rather than where somebody is', () => {
        expect(arrivalMark({ threadId: 'a-conversation', openAt: 'three', fromResult: true }, alone, false)).toBe(
            'result',
        );
    });
});
