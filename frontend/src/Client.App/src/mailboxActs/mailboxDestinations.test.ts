// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailFolder, MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import { destinationsFor, filingFor, folderWithRole, refusalFor } from './mailboxDestinations';
import type { ActedMessage } from './useMailboxActs';

const everythingOffered = { flags: true, moves: true };

function folder(alias: string, role: MailFolderRole | null, path: readonly string[]): MailFolder {
    return {
        alias,
        role,
        path,
        storedEmailCount: 0,
        unreadEmailCount: 0,
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: null,
        behind: false,
    };
}

function directoryOf(accounts: Record<string, readonly MailFolder[]>): MailFolderDirectory {
    return {
        synchronizationEnabled: true,
        accounts: Object.entries(accounts).map(([id, folders]) => ({
            account: {
                id,
                displayName: id,
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
            },
            folders,
        })),
    };
}

const wholeMailbox = directoryOf({
    work: [
        folder('work-inbox', 'Inbox', ['INBOX']),
        folder('work-archive', 'Archive', ['Archive']),
        folder('work-trash', 'Trash', ['Trash']),
        folder('work-clients', null, ['Projects', 'Clients']),
    ],
    home: [folder('home-inbox', 'Inbox', ['INBOX'])],
});

function inWork(storedEmailId: string): ActedMessage {
    return { storedEmailId, account: 'work', folder: 'work-inbox' };
}

const atHome: ActedMessage = { storedEmailId: 'message-9', account: 'home', folder: 'home-inbox' };

describe('folderWithRole', () => {
    it('names the folder an account labels with the role, which is the only thing that says what archiving means', () => {
        expect(folderWithRole(wholeMailbox, 'work', 'Archive')).toBe('work-archive');
    });

    it('names none where the configuration labels none, rather than guessing one from what a folder is called', () => {
        expect(folderWithRole(wholeMailbox, 'home', 'Archive')).toBeNull();
    });

    it('names none for an account the folders never described, and for folders that were never read', () => {
        expect(folderWithRole(wholeMailbox, 'nobody', 'Trash')).toBeNull();
        expect(folderWithRole(null, 'work', 'Trash')).toBeNull();
    });
});

describe('destinationsFor', () => {
    it('offers the folders of the one account the messages are in, named by their place on the server', () => {
        expect(destinationsFor(wholeMailbox, [inWork('message-1')])).toStrictEqual([
            { alias: 'work-archive', name: 'Archive' },
            { alias: 'work-inbox', name: 'INBOX' },
            { alias: 'work-clients', name: 'Projects / Clients' },
            { alias: 'work-trash', name: 'Trash' },
        ]);
    });

    it('offers nothing across two accounts, a folder belonging to the account it is in', () => {
        expect(destinationsFor(wholeMailbox, [inWork('message-1'), atHome])).toStrictEqual([]);
    });
});

describe('refusalFor', () => {
    it('refuses an act about nothing before it asks what the credential may do', () => {
        expect(refusalFor('flag', [], null, { flags: false, moves: false })).toBe('nothingToActOn');
    });

    it.each([
        ['flag', { flags: false, moves: true }],
        ['markUnread', { flags: false, moves: true }],
        ['archive', { flags: true, moves: false }],
        ['delete', { flags: true, moves: false }],
        ['move', { flags: true, moves: false }],
    ] as const)(
        'says a credential without the grant may not %s, rather than letting the act be refused',
        (act, offered) => {
            expect(refusalFor(act, [inWork('message-1')], wholeMailbox, offered)).toBe('notOffered');
        },
    );

    it('permits the two acts that change a flag without asking anything of the folders', () => {
        expect(refusalFor('flag', [inWork('message-1')], null, everythingOffered)).toBeNull();
        expect(refusalFor('markUnread', [inWork('message-1')], null, everythingOffered)).toBeNull();
    });

    it.each([
        ['archive', 'noArchiveFolder'],
        ['delete', 'noTrashFolder'],
    ] as const)('says an account labelling no folder for %s has nowhere to put it', (act, refusal) => {
        expect(refusalFor(act, [atHome], wholeMailbox, everythingOffered)).toBe(refusal);
        expect(refusalFor(act, [inWork('message-1')], wholeMailbox, everythingOffered)).toBeNull();
    });

    it('refuses a move across two accounts, and one with nowhere to file into', () => {
        expect(refusalFor('move', [inWork('message-1'), atHome], wholeMailbox, everythingOffered)).toBe(
            'severalAccounts',
        );
        expect(refusalFor('move', [inWork('message-1')], directoryOf({ work: [] }), everythingOffered)).toBe(
            'noOtherFolder',
        );
    });
});

describe('filingFor', () => {
    it('files each message in its own account’s folder for the role, rather than in one folder for the batch', () => {
        const twoAccounts = directoryOf({
            work: [folder('work-archive', 'Archive', ['Archive'])],
            home: [folder('home-archive', 'Archive', ['Arkiv'])],
        });

        expect(filingFor('archive', [inWork('message-1'), atHome], twoAccounts, null)).toStrictEqual([
            { storedEmailId: 'message-1', destinationFolder: 'work-archive' },
            { storedEmailId: 'message-9', destinationFolder: 'home-archive' },
        ]);
    });

    it('files every message where the move was told to, which is the one folder somebody chose', () => {
        expect(
            filingFor('move', [inWork('message-1'), inWork('message-2')], wholeMailbox, 'work-clients'),
        ).toStrictEqual([
            { storedEmailId: 'message-1', destinationFolder: 'work-clients' },
            { storedEmailId: 'message-2', destinationFolder: 'work-clients' },
        ]);
    });

    it('files nothing for an act that changes a flag, and nothing where the account labels no such folder', () => {
        expect(filingFor('flag', [inWork('message-1')], wholeMailbox, null)).toStrictEqual([]);
        expect(filingFor('archive', [atHome], wholeMailbox, null)).toStrictEqual([]);
    });
});
