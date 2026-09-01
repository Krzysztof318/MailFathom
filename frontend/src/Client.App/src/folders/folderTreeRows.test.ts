// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAccount, MailFolder, MailFolderDirectory } from '@mailfathom/client-backend';
import { folderTreeOf, visibleRows, type FolderTreeRow } from './folderTreeRows';

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

const directory: MailFolderDirectory = {
    synchronizationEnabled: true,
    accounts: [
        {
            account: account('work', 'Work'),
            folders: [
                folder({
                    alias: 'INBOX',
                    role: 'Inbox',
                    path: ['INBOX'],
                    unreadEmailCount: 12,
                    storedEmailCount: 4213,
                }),
                folder({ alias: 'SENT', role: 'Sent', path: ['Wysłane'], storedEmailCount: 300 }),
                folder({ alias: 'ARCHIVE-2024', path: ['Archiwum', '2024'], storedEmailCount: 980 }),
            ],
        },
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

    it('offers the roles the owner’s mailboxes play as scopes spanning all of them', () => {
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

        expect(keysOf(work?.children ?? [])).toEqual(['folder:work:INBOX', 'folder:work:SENT', 'level:work:Archiwum']);
    });

    it('nests a folder where its mail server nests it', () => {
        const archive = find(folderTreeOf(directory), 'level:work:Archiwum');
        const nested = archive?.children[0];

        expect(archive?.scope).toBeNull();
        expect(archive?.level).toBe(2);
        expect(nested?.key).toBe('folder:work:ARCHIVE-2024');
        expect(nested?.name).toBe('2024');
        expect(nested?.level).toBe(3);
    });

    it('shows a folder nothing has bound to a remote folder under the name MailFathom knows it by', () => {
        const news = find(folderTreeOf(directory), 'folder:personal:NEWS');

        expect(news?.name).toBe('NEWS');
        expect(news?.state).toBe('NeverSynchronized');
    });

    it('reads an owner with no mailbox as a tree with no rows rather than as a row with nothing under it', () => {
        expect(folderTreeOf({ synchronizationEnabled: true, accounts: [] })).toEqual([]);
    });
});

describe('visibleRows', () => {
    it('draws every row of an unfolded tree, each knowing where it sits among its siblings', () => {
        const visible = visibleRows(folderTreeOf(directory), new Set());

        expect(visible.map((row) => row.row.key)).toEqual([
            'everything',
            'role:Inbox',
            'role:Sent',
            'account:work',
            'folder:work:INBOX',
            'folder:work:SENT',
            'level:work:Archiwum',
            'folder:work:ARCHIVE-2024',
            'account:personal',
            'folder:personal:INBOX',
            'folder:personal:NEWS',
        ]);

        expect(visible[0]).toEqual(expect.objectContaining({ position: 1, setSize: 3, expanded: true }));
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
