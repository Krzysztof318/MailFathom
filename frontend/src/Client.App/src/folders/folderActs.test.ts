// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { actsOffered, editable } from './folderActs';
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

const mailbox = row({ accountId: 'work' });
const folder = row({ accountId: 'work', alias: 'PROJECTS', remotePath: ['Projects'] });

describe('actsOffered', () => {
    it('offers nothing on a row standing for every mailbox at once, there being no mailbox to act inside', () => {
        expect(actsOffered(row({ accountId: null }))).toEqual([]);
    });

    it('offers nothing on a role spanning every mailbox either', () => {
        expect(actsOffered(row({ accountId: null, role: 'Inbox' }))).toEqual([]);
    });

    it('offers a mailbox making a folder and marking everything in it read', () => {
        expect(actsOffered(mailbox)).toEqual(['newFolder', 'markAllRead']);
    });

    it('offers a folder the whole set, in the order the design project draws them', () => {
        expect(actsOffered(folder)).toEqual(['newFolderInside', 'markAllRead', 'editFolder', 'deleteFolder']);
    });

    it('refuses to edit or remove a folder the deployment files mail by, which is what a role means', () => {
        expect(actsOffered(row({ ...folder, role: 'Inbox' }))).toEqual(['newFolderInside', 'markAllRead']);
    });

    it('stops offering a folder inside at the third level of an alias, which is the tree’s own ceiling', () => {
        const deepest = row({ accountId: 'work', alias: 'INBOX/PROJECTS/2026', remotePath: ['INBOX', 'p', '2026'] });

        expect(actsOffered(deepest)).toEqual(['markAllRead', 'editFolder', 'deleteFolder']);
    });

    it('offers a level of an alias nothing is bound to only the folder that would be made inside it', () => {
        expect(actsOffered(row({ accountId: 'work', alias: 'PROJECTS' }))).toEqual(['newFolderInside']);
    });

    it('offers such a level nothing at all where it already sits at the ceiling', () => {
        expect(actsOffered(row({ accountId: 'work', alias: 'A/B/C' }))).toEqual([]);
    });
});

describe('editable', () => {
    it('says a folder the person made themselves is one they may rename and remove', () => {
        expect(editable(folder)).toBe(true);
    });

    it('says a folder playing a role is not, whatever else it is', () => {
        expect(editable(row({ ...folder, role: 'Trash' }))).toBe(false);
    });

    it('says a level of an alias nothing is bound to is not, there being no declaration to change', () => {
        expect(editable(row({ accountId: 'work', alias: 'PROJECTS' }))).toBe(false);
    });
});
