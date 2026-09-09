// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import {
    accountInScope,
    everything,
    isMailFolderRole,
    roleRank,
    sameScope,
    scopeKey,
    scopeOfAccount,
    scopeReaches,
    scopeStillOffered,
    type MailScope,
} from './mailScope';

const workInbox: MailScope = { kind: 'folder', accountId: 'work', alias: 'INBOX' };

const offered: MailFolderDirectory = {
    synchronizationEnabled: true,
    accounts: [
        {
            account: {
                id: 'work',
                displayName: 'Work',
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                behind: false,
            },
            folders: [
                {
                    alias: 'INBOX',
                    role: 'Inbox',
                    path: ['INBOX'],
                    storedEmailCount: 4213,
                    unreadEmailCount: 12,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                    behind: false,
                },
            ],
        },
    ],
};

describe('scopeKey', () => {
    it.each<{ scope: MailScope; key: string }>([
        { scope: everything, key: 'everything' },
        { scope: { kind: 'role', role: 'Sent' }, key: 'role:Sent' },
        { scope: { kind: 'account', accountId: 'work' }, key: 'account:work' },
        { scope: workInbox, key: 'folder:work:INBOX' },
    ])('identifies $key', ({ scope, key }) => {
        expect(scopeKey(scope)).toBe(key);
    });
});

describe('sameScope', () => {
    it('reads two scopes pointing at one folder as the same one', () => {
        expect(sameScope(workInbox, { kind: 'folder', accountId: 'work', alias: 'INBOX' })).toBe(true);
    });

    it('tells one mailbox’s inbox apart from another’s', () => {
        expect(sameScope(workInbox, { kind: 'folder', accountId: 'personal', alias: 'INBOX' })).toBe(false);
    });

    it('tells a whole mailbox apart from one folder of it', () => {
        expect(sameScope(workInbox, { kind: 'account', accountId: 'work' })).toBe(false);
    });
});

describe('accountInScope', () => {
    it.each<{ scope: MailScope; accountId: string }>([
        { scope: workInbox, accountId: 'work' },
        { scope: { kind: 'account', accountId: 'personal' }, accountId: 'personal' },
    ])('answers the mailbox $accountId a scope names', ({ scope, accountId }) => {
        expect(accountInScope(scope)).toBe(accountId);
    });

    it.each<{ scope: MailScope }>([{ scope: everything }, { scope: { kind: 'role', role: 'Inbox' } }])(
        'answers no mailbox for a scope spanning every one of them',
        ({ scope }) => {
            expect(accountInScope(scope)).toBeNull();
        },
    );
});

describe('scopeOfAccount', () => {
    it('scopes to the whole of a mailbox somebody named', () => {
        expect(scopeOfAccount('work')).toEqual({ kind: 'account', accountId: 'work' });
    });

    it('scopes to everything where nobody named one', () => {
        expect(scopeOfAccount(null)).toEqual(everything);
    });
});

describe('isMailFolderRole', () => {
    it.each(['Inbox', 'Archive', 'Drafts', 'Sent', 'Junk', 'Trash', 'All', 'Flagged', 'Important', 'Outbox'])(
        'reads %s as a role this surface publishes',
        (role) => {
            expect(isMailFolderRole(role)).toBe(true);
        },
    );

    it.each([{ value: 'Spam' }, { value: 'inbox' }, { value: 7 }, { value: null }])(
        'refuses $value, which is not one of them',
        ({ value }) => {
            expect(isMailFolderRole(value)).toBe(false);
        },
    );
});

describe('roleRank', () => {
    it('offers the inbox before everything else', () => {
        expect(roleRank('Inbox')).toBeLessThan(roleRank('Sent'));
    });

    it('offers the five folders a reader reaches for in the order they are reached for', () => {
        const reachedFor: readonly MailFolderRole[] = ['Inbox', 'Sent', 'Drafts', 'Archive', 'Trash'];

        expect([...reachedFor].sort((one, other) => roleRank(one) - roleRank(other))).toEqual(reachedFor);
        expect(roleRank('Trash')).toBeLessThan(roleRank('Junk'));
    });

    it('offers a folder playing no role after every folder that plays one', () => {
        expect(roleRank(null)).toBeGreaterThan(roleRank('Outbox'));
    });
});

describe('scopeReaches', () => {
    it.each<{ scope: MailScope; account: string; folder: string | null; reached: boolean }>([
        { scope: everything, account: 'personal', folder: 'Archive', reached: true },
        { scope: { kind: 'role', role: 'Inbox' }, account: 'personal', folder: 'Archive', reached: true },
        { scope: { kind: 'account', accountId: 'work' }, account: 'work', folder: 'Archive', reached: true },
        { scope: { kind: 'account', accountId: 'work' }, account: 'personal', folder: 'INBOX', reached: false },
        { scope: workInbox, account: 'work', folder: 'INBOX', reached: true },
        { scope: workInbox, account: 'work', folder: 'Archive', reached: false },
        { scope: workInbox, account: 'personal', folder: 'INBOX', reached: false },
    ])('answers $reached for $account', ({ scope, account, folder, reached }) => {
        expect(scopeReaches(scope, account, folder)).toBe(reached);
    });

    it('reaches a folder scope with a change named against the account alone', () => {
        expect(scopeReaches(workInbox, 'work', null)).toBe(true);
    });
});

describe('scopeStillOffered', () => {
    it.each<{ scope: MailScope; still: boolean }>([
        { scope: everything, still: true },
        { scope: { kind: 'role', role: 'Inbox' }, still: true },
        { scope: { kind: 'role', role: 'Junk' }, still: false },
        { scope: { kind: 'account', accountId: 'work' }, still: true },
        { scope: { kind: 'account', accountId: 'personal' }, still: false },
        { scope: workInbox, still: true },
        { scope: { kind: 'folder', accountId: 'work', alias: 'ARCHIVE-2024' }, still: false },
        { scope: { kind: 'folder', accountId: 'personal', alias: 'INBOX' }, still: false },
    ])('answers $still for a scope the deployment does or does not still offer', ({ scope, still }) => {
        expect(scopeStillOffered(scope, offered)).toBe(still);
    });

    // Somebody holding no mailbox at all is told so by the tree drawing the sentence for it, and taking the widest
    // scope away from them would answer that with something nobody can act on instead.
    it('offers everything even where the deployment answered with no account at all', () => {
        expect(scopeStillOffered(everything, { synchronizationEnabled: true, accounts: [] })).toBe(true);
    });
});
