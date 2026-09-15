// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { actsOffered, nothingReported, type ReportedFolderActs } from './folderActs';
import type { FolderTreeRow } from './folderTreeRows';

function row(stated: Partial<FolderTreeRow>): FolderTreeRow {
    return {
        key: 'row',
        scope: null,
        name: 'Whatever',
        role: null,
        accountId: 'work',
        alias: null,
        remotePath: null,
        level: 1,
        opensCollapsed: false,
        unreadEmailCount: null,
        storedEmailCount: null,
        children: [],
        ...stated,
    };
}

/** A mailbox that allows everything, which is what leaves the row itself as the only thing deciding. */
const everything: ReportedFolderActs = { account: ['create'], folder: ['rename', 'move', 'delete'] };

/** A folder the service reports as allowing nothing, which is what it says of one playing a role. */
const nothingOnTheFolder: ReportedFolderActs = { account: ['create'], folder: [] };

const mailbox = row({ accountId: 'work' });
const folder = row({ accountId: 'work', alias: 'PROJECTS', remotePath: ['Projects'] });

describe('actsOffered', () => {
    it('offers nothing on a row standing for every mailbox at once, there being no mailbox to act inside', () => {
        expect(actsOffered(row({ accountId: null }), everything)).toEqual([]);
    });

    it('offers nothing on a role spanning every mailbox either', () => {
        expect(actsOffered(row({ accountId: null, role: 'Inbox' }), everything)).toEqual([]);
    });

    it('offers a mailbox making a folder and marking everything in it read', () => {
        expect(actsOffered(mailbox, everything)).toEqual(['newFolder', 'markAllRead']);
    });

    it('offers a mailbox the service reports as taking no new folder only marking everything read', () => {
        expect(actsOffered(mailbox, { account: [], folder: null })).toEqual(['markAllRead']);
    });

    it('offers a folder the whole set, in the order the design project draws them', () => {
        expect(actsOffered(folder, everything)).toEqual([
            'newFolderInside',
            'markAllRead',
            'editFolder',
            'deleteFolder',
        ]);
    });

    it('offers no act on the folder itself where the service reports it as allowing none', () => {
        expect(actsOffered(folder, nothingOnTheFolder)).toEqual(['newFolderInside', 'markAllRead']);
    });

    it('offers removing a folder that may only be removed, and nothing about its name', () => {
        expect(actsOffered(folder, { account: [], folder: ['delete'] })).toEqual(['markAllRead', 'deleteFolder']);
    });

    it('draws one item for renaming and moving, which the dialog behind it performs as either', () => {
        expect(actsOffered(folder, { account: [], folder: ['move'] })).toEqual(['markAllRead', 'editFolder']);
    });

    it('offers nothing but marking read on a row no report of the mailbox has arrived for', () => {
        expect(actsOffered(folder, nothingReported)).toEqual(['markAllRead']);
    });

    it('stops offering a folder inside at the third level of an alias, which is the tree’s own ceiling', () => {
        const deepest = row({ accountId: 'work', alias: 'INBOX/PROJECTS/2026', remotePath: ['INBOX', 'p', '2026'] });

        expect(actsOffered(deepest, everything)).toEqual(['markAllRead', 'editFolder', 'deleteFolder']);
    });

    it('offers a level of an alias nothing is bound to only the folder that would be made inside it', () => {
        expect(actsOffered(row({ accountId: 'work', alias: 'PROJECTS' }), everything)).toEqual(['newFolderInside']);
    });

    it('offers such a level nothing at all where it already sits at the ceiling', () => {
        expect(actsOffered(row({ accountId: 'work', alias: 'A/B/C' }), everything)).toEqual([]);
    });

    it('offers such a level nothing where the report names no folder to make one beneath', () => {
        expect(
            actsOffered(row({ accountId: 'work', alias: 'PROJECTS' }), { account: ['create'], folder: null }),
        ).toEqual([]);
    });
});
