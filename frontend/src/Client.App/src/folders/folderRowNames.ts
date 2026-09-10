// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';
import { folderRoleLabels } from '../workspace/mailScope';
import type { FolderTreeRow } from './folderTreeRows';

// What a row of the folder column is called, in one place because three surfaces say it: the row itself, the header
// of the menu that row answers a press with, and every toast an act on it raises. A reader told they marked *Archive*
// read by one and *INBOX.Archiwum* by the other has been told about two folders.
//
// It is a module of its own rather than a function inside `FolderRow.tsx` for the reason `frontend/src/AGENTS.md`
// gives about that file's own name: a module Vite hot-reloads may export components alone.

/**
 * The row's name as somebody reads it.
 *
 * A folder playing a role is called by the role — an inbox is an inbox whatever the provider named the folder and in
 * whatever language — the row spanning every mailbox is called that, and everything else is called what its own mail
 * server calls it.
 */
export function folderRowName(row: FolderTreeRow, translate: (key: MessageKey) => string): string {
    if (row.scope?.kind === 'everything') {
        return translate('scope.allMailboxes');
    }

    return row.role === null ? row.name : translate(folderRoleLabels[row.role]);
}
