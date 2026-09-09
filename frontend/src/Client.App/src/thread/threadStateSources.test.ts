// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailThreadMessage } from '@mailfathom/client-backend';
import { sourceOf } from './threadStateSources';

function message(
    id: string,
    position: number,
    sender: { readonly displayName?: string | null; readonly address?: string | null } = {},
): MailThreadMessage {
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
            sentAt: null,
            senderAddress: sender.address ?? 'auditor@example.invalid',
            senderDisplayName: sender.displayName === undefined ? 'Karolina Nowak' : sender.displayName,
            toAddresses: [],
            unread: false,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            sizeOctets: 512,
            preview: null,
            threadMessageCount: null,
        },
        message: null,
        body: null,
    };
}

const held: readonly MailThreadMessage[] = [message('one', 0), message('two', 1)];

describe('sourceOf', () => {
    it('counts the message from one, as a reader counts them', () => {
        expect(sourceOf(held, 'two')?.position).toBe(2);
    });

    it('names the message by the leading word of who wrote it, which is what the card has room for', () => {
        expect(sourceOf(held, 'one')?.name).toBe('Karolina');
    });

    it('names a message whose sender was never named by the address it came from', () => {
        const anonymous = [message('one', 0, { displayName: null, address: 'noreply@example.invalid' })];

        expect(sourceOf(anonymous, 'one')?.name).toBe('noreply@example.invalid');
    });

    // The wire type admits a display name of no characters, and a link named by it would announce nothing.
    it('names a message whose sender was named as nothing by the address it came from', () => {
        const blank = [message('one', 0, { displayName: '', address: 'noreply@example.invalid' })];

        expect(sourceOf(blank, 'one')?.name).toBe('noreply@example.invalid');
    });

    it('carries the message on, so following the source names what to reveal', () => {
        expect(sourceOf(held, 'two')?.storedEmailId).toBe('two');
    });

    // A statement may rest on a message a conversation has not paged in yet, and a link that reveals nothing is worse
    // than no link at all.
    it('resolves a message this conversation does not hold to nothing', () => {
        expect(sourceOf(held, 'three')).toBeNull();
    });

    it('resolves a statement naming no message at all to nothing', () => {
        expect(sourceOf(held, undefined)).toBeNull();
    });
});
