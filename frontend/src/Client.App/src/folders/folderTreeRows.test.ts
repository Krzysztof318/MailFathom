// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAccount, MailAccountFolders, MailFolder, MailFolderDirectory } from '@mailfathom/client-backend';
import { folderTreeOf, openingScope, visibleRows, type FolderTreeRow } from './folderTreeRows';

function account(id: string, displayName: string): MailAccount {
    return {
        id,
        displayName,
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
        behind: false,
    };
}

function folder(folder: Partial<MailFolder> & Pick<MailFolder, 'alias'>): MailFolder {
    return {
        role: null,
        path: [folder.alias],
        storedEmailCount: 0,
        unreadEmailCount: 0,
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
        behind: false,
        ...folder,
    };
}

const work: MailAccountFolders = {
    account: account('work', 'Work'),
    folders: [
        folder({ alias: 'INBOX', role: 'Inbox', path: ['INBOX'], unreadEmailCount: 12, storedEmailCount: 4213 }),
        folder({ alias: 'SENT', role: 'Sent', path: ['Wysłane'], storedEmailCount: 300 }),
        folder({ alias: 'ARCHIVE/2024', path: ['Archiwum', '2024'], storedEmailCount: 980 }),
    ],
};

/** The one mailbox above and nothing else, which is the case with no group spanning every mailbox. */
const alone: MailFolderDirectory = { synchronizationEnabled: true, accounts: [work] };

const directory: MailFolderDirectory = {
    synchronizationEnabled: true,
    accounts: [
        work,
        {
            account: account('personal', 'Personal'),
            folders: [
                folder({ alias: 'INBOX', role: 'Inbox', path: ['INBOX'], unreadEmailCount: 3, storedEmailCount: 50 }),
                folder({ alias: 'NEWS', path: [], synchronizationState: 'NeverSynchronized' }),
            ],
        },
    ],
};

function keysOf(rows: readonly FolderTreeRow[]): readonly string[] {
    return rows.map((row) => row.key);
}

function find(rows: readonly FolderTreeRow[], key: string): FolderTreeRow | undefined {
    for (const row of rows) {
        const found = row.key === key ? row : find(row.children, key);

        if (found !== undefined) {
            return found;
        }
    }

    return undefined;
}

describe('folderTreeOf', () => {
    it('opens with every mailbox at once, and then each of them', () => {
        expect(keysOf(folderTreeOf(directory))).toEqual(['everything', 'account:work', 'account:personal']);
    });

    it('offers the roles the user’s mailboxes play as scopes spanning all of them', () => {
        const rows = folderTreeOf(directory);

        expect(keysOf(find(rows, 'everything')?.children ?? [])).toEqual(['role:Inbox', 'role:Sent']);
    });

    it('counts a role across every mailbox playing it, because that is what selecting it would show', () => {
        const inbox = find(folderTreeOf(directory), 'role:Inbox');

        expect(inbox?.unreadEmailCount).toBe(15);
        expect(inbox?.storedEmailCount).toBe(4263);
    });

    it('names a folder by the role its deployment gave it rather than by what its server calls the folder', () => {
        const sent = find(folderTreeOf(directory), 'folder:work:SENT');

        expect(sent?.role).toBe('Sent');
        expect(sent?.name).toBe('Wysłane');
    });

    it('places the folders playing a role before the ones playing none, in the order they are offered in', () => {
        const work = find(folderTreeOf(directory), 'account:work');

        expect(keysOf(work?.children ?? [])).toEqual(['folder:work:INBOX', 'folder:work:SENT', 'level:work:ARCHIVE']);
    });

    it('nests a folder where its alias nests it, and names it by the level its mail server calls it', () => {
        const archive = find(folderTreeOf(directory), 'level:work:ARCHIVE');
        const nested = archive?.children[0];

        expect(archive?.scope).toBeNull();
        expect(archive?.level).toBe(2);
        expect(nested?.key).toBe('folder:work:ARCHIVE/2024');
        expect(nested?.name).toBe('2024');
        expect(nested?.level).toBe(3);
    });

    // The alias is upper-cased by the service, so a level read off it would draw a mailbox in capitals nobody typed.
    it('names a level nothing is bound to by what the mail server calls it rather than by its alias segment', () => {
        expect(find(folderTreeOf(directory), 'level:work:ARCHIVE')?.name).toBe('Archiwum');
    });

    it('falls back to the alias segment for a level nothing beneath it states a remote path for', () => {
        const unbound = {
            synchronizationEnabled: true,
            accounts: [
                {
                    account: account('work', 'Work'),
                    folders: [folder({ alias: 'ARCHIVE/2024', path: [] })],
                },
            ],
        };

        expect(find(folderTreeOf(unbound), 'level:work:ARCHIVE')?.name).toBe('ARCHIVE');
    });

    it('shows a folder nothing has bound to a remote folder under the name MailFathom knows it by', () => {
        expect(find(folderTreeOf(directory), 'folder:personal:NEWS')?.name).toBe('NEWS');
    });

    it('offers the special folders before the rest, in the order a reader reaches for them', () => {
        const special = {
            synchronizationEnabled: true,
            accounts: [
                {
                    account: account('work', 'Work'),
                    folders: [
                        folder({ alias: 'ARCHIVE', role: 'Archive', path: ['Archive'] }),
                        folder({ alias: 'TRASH', role: 'Trash', path: ['Trash'] }),
                        folder({ alias: 'DRAFTS', role: 'Drafts', path: ['Drafts'] }),
                        folder({ alias: 'SENT', role: 'Sent', path: ['Sent'] }),
                        folder({ alias: 'INBOX', role: 'Inbox', path: ['INBOX'] }),
                    ],
                },
                { account: account('personal', 'Personal'), folders: [] },
            ],
        };

        expect(keysOf(find(folderTreeOf(special), 'everything')?.children ?? [])).toEqual([
            'role:Inbox',
            'role:Sent',
            'role:Drafts',
            'role:Archive',
            'role:Trash',
        ]);

        expect(keysOf(find(folderTreeOf(special), 'account:work')?.children ?? [])).toEqual([
            'folder:work:INBOX',
            'folder:work:SENT',
            'folder:work:DRAFTS',
            'folder:work:ARCHIVE',
            'folder:work:TRASH',
        ]);
    });

    it('scopes a mailbox’s own row to its inbox, because that is what pressing a mailbox’s name means', () => {
        expect(find(folderTreeOf(directory), 'account:work')?.scope).toEqual({
            kind: 'folder',
            accountId: 'work',
            alias: 'INBOX',
        });
    });

    it('scopes a mailbox with no inbox to the mailbox itself', () => {
        const inboxless = {
            synchronizationEnabled: true,
            accounts: [
                { account: account('work', 'Work'), folders: [folder({ alias: 'NEWS', path: ['NEWS'] })] },
                { account: account('personal', 'Personal'), folders: [] },
            ],
        };

        expect(find(folderTreeOf(inboxless), 'account:work')?.scope).toEqual({ kind: 'account', accountId: 'work' });
    });

    it('offers no group spanning every mailbox to a user who has exactly one', () => {
        expect(keysOf(folderTreeOf(alone))).toEqual(['account:work']);
    });

    it('reads a user with no mailbox as a tree with no rows rather than as a row with nothing under it', () => {
        expect(folderTreeOf({ synchronizationEnabled: true, accounts: [] })).toEqual([]);
    });
});

describe('openingScope', () => {
    it('opens on every mailbox’s inbox at once where the tree draws a row for that', () => {
        expect(openingScope(directory)).toEqual({ kind: 'role', role: 'Inbox' });
    });

    it('opens on every mailbox at once where no mailbox plays an inbox to open on', () => {
        const inboxless = {
            synchronizationEnabled: true,
            accounts: [
                { account: account('work', 'Work'), folders: [folder({ alias: 'NEWS', path: ['NEWS'] })] },
                { account: account('personal', 'Personal'), folders: [] },
            ],
        };

        expect(openingScope(inboxless)).toEqual({ kind: 'everything' });
    });

    it('opens on the one mailbox’s inbox where there is no row spanning every mailbox', () => {
        expect(openingScope(alone)).toEqual({ kind: 'folder', accountId: 'work', alias: 'INBOX' });
    });

    it('opens on the one mailbox itself where it plays no inbox', () => {
        const inboxless = {
            synchronizationEnabled: true,
            accounts: [{ account: account('work', 'Work'), folders: [folder({ alias: 'NEWS', path: ['NEWS'] })] }],
        };

        expect(openingScope(inboxless)).toEqual({ kind: 'account', accountId: 'work' });
    });

    it('opens on every mailbox at once where the user has none, which is the widest scope rather than a place', () => {
        expect(openingScope({ synchronizationEnabled: true, accounts: [] })).toEqual({ kind: 'everything' });
    });
});

describe('visibleRows', () => {
    it('opens the mailboxes and leaves what nests inside a folder shut, each row knowing where it sits', () => {
        const visible = visibleRows(folderTreeOf(directory), new Set());

        expect(visible.map((row) => row.row.key)).toEqual([
            'everything',
            'role:Inbox',
            'role:Sent',
            'account:work',
            'folder:work:INBOX',
            'folder:work:SENT',
            'level:work:ARCHIVE',
            'account:personal',
            'folder:personal:INBOX',
            'folder:personal:NEWS',
        ]);

        expect(visible[0]).toEqual(expect.objectContaining({ position: 1, setSize: 3, expanded: true }));
    });

    it('draws what nests inside a folder somebody opened, which is the same set read the other way', () => {
        const visible = visibleRows(folderTreeOf(directory), new Set(['level:work:ARCHIVE']));

        expect(visible.map((row) => row.row.key)).toContain('folder:work:ARCHIVE/2024');
    });

    it('leaves out what is folded away, and everything under it', () => {
        const visible = visibleRows(folderTreeOf(directory), new Set(['everything', 'account:work']));

        expect(visible.map((row) => row.row.key)).toEqual([
            'everything',
            'account:work',
            'account:personal',
            'folder:personal:INBOX',
            'folder:personal:NEWS',
        ]);
    });

    it('says a row with nothing under it has nothing to open, rather than saying it is shut', () => {
        const visible = visibleRows(folderTreeOf(directory), new Set());

        expect(visible.find((row) => row.row.key === 'folder:personal:NEWS')?.expanded).toBeNull();
    });
});
