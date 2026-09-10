// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { en, type MessageKey } from '../localization/en';
import { folderRowName } from './folderRowNames';
import type { FolderTreeRow } from './folderTreeRows';

// The real catalogue rather than a stub answering with its own key: what this module decides is which of three
// things a reader is shown, and a translate that answered `folder.archive` would prove the branch without proving
// that a word ever came out of it.
function translate(key: MessageKey): string {
    return en[key];
}

function row(stated: Partial<FolderTreeRow>): FolderTreeRow {
    return {
        key: 'row',
        scope: null,
        name: 'INBOX.Archiwum',
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

describe('folderRowName', () => {
    it('calls the row spanning every mailbox by what that scope is called', () => {
        expect(folderRowName(row({ scope: { kind: 'everything' } }), translate)).toBe('All mailboxes');
    });

    it('calls a folder playing a role by the role rather than by what its mail server named it', () => {
        expect(folderRowName(row({ role: 'Archive', name: 'INBOX.Archiwum' }), translate)).toBe('Archive');
    });

    it('calls a folder playing no role by its own name', () => {
        expect(folderRowName(row({ name: 'Contracts 2027' }), translate)).toBe('Contracts 2027');
    });
});
