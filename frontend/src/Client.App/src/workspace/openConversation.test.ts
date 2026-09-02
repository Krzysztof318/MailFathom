// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { conversationKey } from './openConversation';

describe('conversationKey', () => {
    it('tells one conversation from another', () => {
        expect(conversationKey({ threadId: 'one', openAt: null })).not.toBe(
            conversationKey({ threadId: 'two', openAt: null }),
        );
    });

    it('tells one conversation opened at two different messages apart, because each opens at a different place', () => {
        expect(conversationKey({ threadId: 'one', openAt: 'a-message' })).not.toBe(
            conversationKey({ threadId: 'one', openAt: 'another-message' }),
        );
    });

    it('tells a conversation opened at a message from the same one opened at none', () => {
        expect(conversationKey({ threadId: 'one', openAt: 'a-message' })).not.toBe(
            conversationKey({ threadId: 'one', openAt: null }),
        );
    });

    it('answers the same conversation opened the same way with the same key', () => {
        expect(conversationKey({ threadId: 'one', openAt: 'a-message' })).toBe(
            conversationKey({ threadId: 'one', openAt: 'a-message' }),
        );
    });
});
