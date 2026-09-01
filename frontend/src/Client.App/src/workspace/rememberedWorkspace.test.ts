// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { rememberedWorkspace, rememberWorkspace } from './rememberedWorkspace';
import { emptyWorkspace, type Workspace } from './useWorkspace';

const storageKey = 'mailfathom.workspace';

const kept: Workspace = {
    scope: { kind: 'folder', accountId: 'work', alias: 'INBOX' },
    collapsed: ['account:personal'],
    selection: 'AAMkAD-42',
    question: 'what did Nordwind send',
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
});

describe('rememberedWorkspace', () => {
    it('answers an empty workspace where nothing was kept', () => {
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
    ])('opens on nothing rather than on $shape', ({ value }) => {
        window.sessionStorage.setItem(storageKey, value);

        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });
});
