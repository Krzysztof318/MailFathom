// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { actsOffered, nothingReported, type ReportedFolderActs } from './folderActs';
import type { FolderTreeRow } from './folderTreeRows';

function row(stated: Partial<FolderTreeRow>): FolderTreeRow {
    return {
        key: 'row',
        scope: { kind: 'account', accountId: 'work' },
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

    it('stops offering a folder inside at the third level of a path, which is the tree’s own ceiling', () => {
        const deepest = row({ accountId: 'work', alias: '2026', remotePath: ['INBOX', 'Projects', '2026'] });

        expect(actsOffered(deepest, everything)).toEqual(['markAllRead', 'editFolder', 'deleteFolder']);
    });

    it('offers a level of a path nothing is declared at nothing at all, there being no folder an act could name', () => {
        const level = row({ key: 'level:work:["Projects"]', scope: null, accountId: 'work', name: 'Projects' });

        expect(actsOffered(level, everything)).toEqual([]);
    });

    it('offers a folder inside one sitting a level down whatever its alias says, since the alias names no place', () => {
        const nested = row({ accountId: 'work', alias: 'SUPPLIERS', remotePath: ['Archive', 'Suppliers'] });

        expect(actsOffered(nested, everything)).toContain('newFolderInside');
    });
});
