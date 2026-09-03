// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { rememberedWorkspace, rememberWorkspace } from './rememberedWorkspace';
import { emptyWorkspace, type Workspace } from './useWorkspace';

const storageKey = 'mailfathom.workspace';

const kept: Workspace = {
    scope: { kind: 'folder', accountId: 'work', alias: 'INBOX' },
    collapsed: ['account:personal'],
    mailboxesFolded: true,
    selection: 'AAMkAD-42',
    conversation: { threadId: '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d', openAt: 'AAMkAD-42' },
    fragment: null,
    selected: ['AAMkAD-42', 'AAMkAD-43'],
    question: 'what did Nordwind send',
    recentSearches: ['quarterly figures'],
};

function stored(value: unknown): void {
    window.sessionStorage.setItem(storageKey, JSON.stringify(value));
}

// The store is one per file rather than one per test, so what one test kept would be what the next one read back.
afterEach(() => {
    window.sessionStorage.clear();
});

describe('rememberWorkspace', () => {
    it('keeps a workspace the next start reads back whole', () => {
        rememberWorkspace(kept);

        expect(rememberedWorkspace()).toEqual(kept);
    });

    // The one part of the workspace that is mail rather than a name for one, so it is the one part a store never sees.
    it('keeps no part of the message somebody had selected', () => {
        rememberWorkspace({ ...kept, fragment: 'the part of the message somebody pointed at' });

        expect(window.sessionStorage.getItem(storageKey)).not.toContain('somebody pointed at');
        expect(rememberedWorkspace().fragment).toBeNull();
    });
});

describe('rememberedWorkspace', () => {
    it('answers an empty workspace where nothing was kept', () => {
        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });

    // A workspace this client wrote before it kept searches at all is one it wrote, so it opens on what was kept
    // rather than on nothing.
    it('reads a workspace kept before searches were offered back as one with none', () => {
        const { recentSearches, ...before } = kept;

        stored(before);

        expect(rememberedWorkspace()).toEqual({ ...kept, recentSearches: [] });
        expect(recentSearches).toHaveLength(1);
    });

    // A workspace kept before the column could be folded is one this client wrote, so it opens on what was kept with
    // the column at the width every workspace before it was drawn at.
    it('reads a workspace kept before the column could fold as one drawn at the column width', () => {
        const { mailboxesFolded, ...before } = kept;

        stored(before);

        expect(rememberedWorkspace()).toEqual({ ...kept, mailboxesFolded: false });
        expect(mailboxesFolded).toBe(true);
    });

    it('refuses a folded column that is not one, a store being a place a person can write', () => {
        stored({ ...emptyWorkspace, mailboxesFolded: 'yes' });

        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });

    it.each([
        { kind: 'everything' },
        { kind: 'role', role: 'Sent' },
        { kind: 'account', accountId: 'work' },
        { kind: 'folder', accountId: 'work', alias: 'ARCHIVE-2024' },
    ])('reads back the scope %o a person had chosen', (scope) => {
        stored({ ...emptyWorkspace, scope });

        expect(rememberedWorkspace().scope).toEqual(scope);
    });

    it.each([
        { shape: 'something that is not JSON at all', value: 'workspace' },
        { shape: 'a workspace that is not an object', value: JSON.stringify([]) },
        { shape: 'a scope naming something this client cannot show', value: JSON.stringify({ scope: 'INBOX' }) },
        {
            shape: 'a scope of a kind this client does not have',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'everywhere' } }),
        },
        {
            shape: 'a role this surface does not publish',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'role', role: 'Spam' } }),
        },
        {
            shape: 'a folder scope naming no account',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'folder', alias: 'INBOX' } }),
        },
        {
            shape: 'an account identifier longer than any the service assigned',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'account', accountId: 'a'.repeat(257) } }),
        },
        {
            shape: 'a folder alias longer than any the service assigned',
            value: JSON.stringify({
                ...emptyWorkspace,
                scope: { kind: 'folder', accountId: 'work', alias: 'a'.repeat(257) },
            }),
        },
        {
            shape: 'folded rows that are not rows',
            value: JSON.stringify({ ...emptyWorkspace, collapsed: [42] }),
        },
        {
            shape: 'a folded row longer than any key this client writes',
            value: JSON.stringify({ ...emptyWorkspace, collapsed: ['a'.repeat(1_025)] }),
        },
        {
            shape: 'more folded rows than a tree has',
            value: JSON.stringify({ ...emptyWorkspace, collapsed: Array.from({ length: 513 }, () => 'account:work') }),
        },
        {
            shape: 'a question longer than anybody typed',
            value: JSON.stringify({ ...emptyWorkspace, question: 'a'.repeat(4_097) }),
        },
        {
            shape: 'a selection that is not an identifier',
            value: JSON.stringify({ ...emptyWorkspace, selection: 7 }),
        },
        {
            shape: 'a selection longer than any identifier the client wrote there',
            value: JSON.stringify({ ...emptyWorkspace, selection: 'a'.repeat(257) }),
        },
        {
            shape: 'messages picked out as something other than a list of them',
            value: JSON.stringify({ ...emptyWorkspace, selected: 'message-1' }),
        },
        {
            shape: 'a message picked out that is not an identifier',
            value: JSON.stringify({ ...emptyWorkspace, selected: [7] }),
        },
        {
            shape: 'a picked-out identifier longer than any the client wrote there',
            value: JSON.stringify({ ...emptyWorkspace, selected: ['a'.repeat(257)] }),
        },
        {
            shape: 'searches kept as something other than a list of them',
            value: JSON.stringify({ ...emptyWorkspace, recentSearches: 'invoice' }),
        },
        {
            shape: 'a kept search that is not text',
            value: JSON.stringify({ ...emptyWorkspace, recentSearches: [7] }),
        },
        {
            shape: 'a kept search longer than this surface ranks against',
            value: JSON.stringify({ ...emptyWorkspace, recentSearches: ['a'.repeat(513)] }),
        },
        {
            shape: 'more kept searches than one tab offers back',
            value: JSON.stringify({
                ...emptyWorkspace,
                recentSearches: Array.from({ length: 9 }, (_, at) => `search-${String(at)}`),
            }),
        },
        {
            shape: 'more messages picked out than one question may be asked about',
            value: JSON.stringify({
                ...emptyWorkspace,
                selected: Array.from({ length: 1_025 }, (_, at) => `message-${String(at)}`),
            }),
        },
        {
            shape: 'a conversation that is not a conversation',
            value: JSON.stringify({ ...emptyWorkspace, conversation: 'a-conversation' }),
        },
        {
            shape: 'a conversation naming no thread',
            value: JSON.stringify({ ...emptyWorkspace, conversation: { openAt: 'message-1' } }),
        },
        {
            shape: 'a conversation opened at something longer than any identifier the client wrote there',
            value: JSON.stringify({
                ...emptyWorkspace,
                conversation: { threadId: 'a-conversation', openAt: 'a'.repeat(257) },
            }),
        },
    ])('opens on nothing rather than on $shape', ({ value }) => {
        window.sessionStorage.setItem(storageKey, value);

        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });

    it('opens on the conversation this tab had open, at the message it was opened at', () => {
        rememberWorkspace(kept);

        expect(rememberedWorkspace().conversation).toEqual({
            threadId: '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d',
            openAt: 'AAMkAD-42',
        });
    });

    it('opens on no conversation where a tab was reading one message', () => {
        rememberWorkspace({ ...kept, conversation: null });

        expect(rememberedWorkspace().conversation).toBeNull();
    });
});
