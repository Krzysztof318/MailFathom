// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAccount, MailFolder, MailFolderDirectory } from '@mailfathom/client-backend';
import type { MarkedIn } from '../readMarking/useReadMarking';
import { unreadAfterMarking } from './unreadAfterMarking';

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
                folder({ alias: 'INBOX', role: 'Inbox', unreadEmailCount: 12 }),
                folder({ alias: 'ARCHIVE', unreadEmailCount: 2 }),
            ],
        },
        {
            account: account('personal', 'Personal'),
            folders: [folder({ alias: 'INBOX', role: 'Inbox', unreadEmailCount: 3 })],
        },
    ],
};

function marking(...marked: readonly (readonly [string, MarkedIn])[]): ReadonlyMap<string, MarkedIn> {
    return new Map(marked);
}

function unreadIn(corrected: MailFolderDirectory, account: string, alias: string): number | undefined {
    return corrected.accounts
        .find((entry) => entry.account.id === account)
        ?.folders.find((folder) => folder.alias === alias)?.unreadEmailCount;
}

describe('unreadAfterMarking', () => {
    it('takes what this client has marked read off the folder it was counted in', () => {
        const corrected = unreadAfterMarking(
            directory,
            marking(['first', { account: 'work', folder: 'INBOX' }], ['second', { account: 'work', folder: 'INBOX' }]),
        );

        expect(unreadIn(corrected, 'work', 'INBOX')).toBe(10);
    });

    // Two mailboxes name a folder the same way, and a count keyed by the name alone would take one person's reading
    // off the other's inbox.
    it('leaves a folder of the same name in another account alone', () => {
        const corrected = unreadAfterMarking(directory, marking(['first', { account: 'work', folder: 'INBOX' }]));

        expect(unreadIn(corrected, 'personal', 'INBOX')).toBe(3);
        expect(unreadIn(corrected, 'work', 'ARCHIVE')).toBe(2);
    });

    // A folder synchronized between the marking and this read already reports the message as read, so subtracting
    // again would count it twice — and a count below nothing is a count nobody can read.
    it('never takes a folder below nothing', () => {
        const corrected = unreadAfterMarking(
            directory,
            marking(
                ['first', { account: 'personal', folder: 'INBOX' }],
                ['second', { account: 'personal', folder: 'INBOX' }],
                ['third', { account: 'personal', folder: 'INBOX' }],
                ['fourth', { account: 'personal', folder: 'INBOX' }],
            ),
        );

        expect(unreadIn(corrected, 'personal', 'INBOX')).toBe(0);
    });

    it('answers the directory it was given where nothing has been marked', () => {
        expect(unreadAfterMarking(directory, marking())).toBe(directory);
    });

    it('leaves a folder this client has marked nothing in exactly as the deployment answered', () => {
        const corrected = unreadAfterMarking(directory, marking(['first', { account: 'work', folder: 'DRAFTS' }]));

        expect(unreadIn(corrected, 'work', 'INBOX')).toBe(12);
        expect(unreadIn(corrected, 'work', 'ARCHIVE')).toBe(2);
    });
});
