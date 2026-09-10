// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { admitsNesting, type FolderTreeRow } from './folderTreeRows';

// What a row of the folder tree answers a press with, as a list of names rather than as a menu. It is a function over
// one row because every one of the four rules is a property of that row, and because a rule read as a value is a rule
// a test can hold the whole set against — which is what *this control is drawn on exactly these rows* needs.
//
// Four rules, and each is somebody's answer rather than this module's. **A row spanning every account offers
// nothing**, because a folder is made, marked and removed inside one mailbox and a row standing for all of them
// stands for no place to put one. **Nesting stops at the third segment of an alias**, which is the tree's ceiling
// asked of the row a folder would be made under. **A folder playing a role is neither edited nor removed**, because
// the role is what the deployment files mail by and a person renaming their inbox would be renaming the destination
// of a rule. And **a level of an alias nothing is bound to is not a folder**, so the only act it carries is making
// one inside it.

/** One thing a row of the folder tree offers, named by what it does rather than by the control that draws it. */
export type FolderAct = 'newFolder' | 'newFolderInside' | 'markAllRead' | 'editFolder' | 'deleteFolder';

/**
 * What one row offers, in the order the design project draws them.
 *
 * @param row The row the gesture happened on.
 * @returns The acts, empty for a row that offers none — which is what says no menu opens at all.
 */
export function actsOffered(row: FolderTreeRow): readonly FolderAct[] {
    // Every act names one mailbox, so the two rows that span them all — the whole workspace, and a role across it —
    // carry none. That is the design project's *All accounts carries none*, and it falls out of the row rather than
    // being asked about the row's kind.
    if (row.accountId === null) {
        return [];
    }

    if (row.alias === null) {
        return ['newFolder', 'markAllRead'];
    }

    if (row.remotePath === null) {
        return admitsNesting(row.alias) ? ['newFolderInside'] : [];
    }

    return [
        ...(admitsNesting(row.alias) ? (['newFolderInside'] as const) : []),
        'markAllRead',
        ...(row.role === null ? (['editFolder', 'deleteFolder'] as const) : []),
    ];
}

/** Whether the folder a row stands for is one the person may rename or remove, which is the one they made themselves. */
export function editable(row: FolderTreeRow): boolean {
    return row.accountId !== null && row.alias !== null && row.remotePath !== null && row.role === null;
}
