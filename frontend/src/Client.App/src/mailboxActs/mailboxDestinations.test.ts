// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailFolder, MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import { deletesPermanently, destinationsFor, filingFor, folderWithRole, refusalFor } from './mailboxDestinations';
import type { ActedMessage } from './useMailboxActs';

const everythingOffered = { flags: true, moves: true, deletes: true };

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
    return { storedEmailId, account: 'work', folder: 'work-inbox', unread: false };
}

const atHome: ActedMessage = { storedEmailId: 'message-9', account: 'home', folder: 'home-inbox', unread: false };

function inWorkTrash(storedEmailId: string): ActedMessage {
    return { storedEmailId, account: 'work', folder: 'work-trash', unread: false };
}

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
    it('offers the one account the messages are in, its folders named by their role and their place on the server', () => {
        expect(destinationsFor(wholeMailbox, [inWork('message-1')])).toStrictEqual([
            {
                accountId: 'work',
                accountName: 'work',
                ordinal: 1,
                destinations: [
                    { alias: 'work-inbox', name: 'INBOX', role: 'Inbox' },
                    { alias: 'work-archive', name: 'Archive', role: 'Archive' },
                    { alias: 'work-trash', name: 'Trash', role: 'Trash' },
                    { alias: 'work-clients', name: 'Projects / Clients', role: null },
                ],
            },
        ]);
    });

    it('counts the account the way the folder column counts its groups, so a mailbox keeps its colour', () => {
        const oneAccount = directoryOf({ work: [folder('work-inbox', 'Inbox', ['INBOX'])] });

        expect(destinationsFor(oneAccount, [inWork('message-1')])[0]?.ordinal).toBe(0);
    });

    it('offers nothing across two accounts, a folder belonging to the account it is in', () => {
        expect(destinationsFor(wholeMailbox, [inWork('message-1'), atHome])).toStrictEqual([]);
    });
});

describe('refusalFor', () => {
    it('refuses an act about nothing before it asks what the credential may do', () => {
        expect(refusalFor('flag', [], null, { flags: false, moves: false, deletes: false })).toBe('nothingToActOn');
    });

    it.each([
        ['flag', { flags: false, moves: true, deletes: true }],
        ['unflag', { flags: false, moves: true, deletes: true }],
        ['markUnread', { flags: false, moves: true, deletes: true }],
        ['archive', { flags: true, moves: false, deletes: true }],
        ['delete', { flags: true, moves: false, deletes: true }],
        ['move', { flags: true, moves: false, deletes: true }],
    ] as const)(
        'says a credential without the grant may not %s, rather than letting the act be refused',
        (act, offered) => {
            expect(refusalFor(act, [inWork('message-1')], wholeMailbox, offered)).toBe('notOffered');
        },
    );

    it('permits the three acts that change a flag without asking anything of the folders', () => {
        expect(refusalFor('flag', [inWork('message-1')], null, everythingOffered)).toBeNull();
        expect(refusalFor('unflag', [inWork('message-1')], null, everythingOffered)).toBeNull();
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

    it('refuses a move for an account whose only folder is the one the messages are already in', () => {
        const oneFolder = directoryOf({ work: [folder('work-inbox', 'Inbox', ['INBOX'])] });

        expect(refusalFor('move', [inWork('message-1')], oneFolder, everythingOffered)).toBe('noOtherFolder');
    });

    it('offers a move where a folder would take at least one of the messages somewhere it is not', () => {
        const twoFolders = directoryOf({
            work: [folder('work-inbox', 'Inbox', ['INBOX']), folder('work-clients', null, ['Clients'])],
        });
        const filed: ActedMessage = {
            storedEmailId: 'message-2',
            account: 'work',
            folder: 'work-clients',
            unread: false,
        };

        expect(refusalFor('move', [inWork('message-1'), filed], twoFolders, everythingOffered)).toBeNull();
    });

    it.each(['archive', 'delete', 'move'] as const)(
        'says the folders are unread rather than blaming the account for %s, which are different things',
        (act) => {
            expect(refusalFor(act, [inWork('message-1')], null, everythingOffered)).toBe('foldersUnknown');
        },
    );

    it('says a credential holding neither grant may not act, rather than offering a folder read nobody would make', () => {
        expect(refusalFor('delete', [inWork('message-1')], null, { flags: true, moves: false, deletes: false })).toBe(
            'notOffered',
        );
    });

    it('reaches a delete in the trash under the deleting grant rather than the moving one', () => {
        expect(
            refusalFor('delete', [inWorkTrash('message-1')], wholeMailbox, {
                flags: false,
                moves: false,
                deletes: true,
            }),
        ).toBeNull();
    });

    it('refuses a delete in the trash for a credential that may file mail but not destroy it', () => {
        expect(
            refusalFor('delete', [inWorkTrash('message-1')], wholeMailbox, {
                flags: false,
                moves: true,
                deletes: false,
            }),
        ).toBe('notOffered');
    });
});

describe('deletesPermanently', () => {
    it('reads a message already in its own account trash as one the act would destroy', () => {
        expect(deletesPermanently(wholeMailbox, [inWorkTrash('message-1')])).toBe(true);
    });

    it('reads a message anywhere else as one the act would file, which is what makes it reversible', () => {
        expect(deletesPermanently(wholeMailbox, [inWork('message-1')])).toBe(false);
    });

    it('takes the reversible reading of a selection reaching outside the trash, rather than two acts in one press', () => {
        expect(deletesPermanently(wholeMailbox, [inWorkTrash('message-1'), inWork('message-2')])).toBe(false);
    });

    it('reads a trash folder as its own account, so an alias another account uses decides nothing', () => {
        const elsewhere: ActedMessage = {
            storedEmailId: 'message-3',
            account: 'home',
            folder: 'work-trash',
            unread: false,
        };

        expect(deletesPermanently(wholeMailbox, [elsewhere])).toBe(false);
    });

    it('destroys nothing where the folders were never read, and nothing where there is nothing to act on', () => {
        expect(deletesPermanently(null, [inWorkTrash('message-1')])).toBe(false);
        expect(deletesPermanently(wholeMailbox, [])).toBe(false);
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
