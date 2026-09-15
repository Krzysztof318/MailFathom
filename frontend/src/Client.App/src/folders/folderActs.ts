// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ManagedMailFolderAct } from '@mailfathom/client-backend';
import { admitsNesting, type FolderTreeRow } from './folderTreeRows';

// What a row of the folder tree answers a press with, as a list of names rather than as a menu. It is a function over
// one row and what the service said about it, because a rule read as a value is a rule a test can hold the whole set
// against — which is what *this control is drawn on exactly these rows* needs.
//
// **Which acts a folder allows is the service's answer and never this client's.** An account's mail may be kept on its
// mail server or by MailFathom itself, and which of the two decides what may be done to a folder — so a client working
// it out from the row would offer acts the service is going to refuse, and would be wrong about the first account
// whose storage changed under it. The report is read, and nothing here reconstructs it.
//
// What is left here is what the report has no opinion about, because it is about this *column* rather than about the
// mailbox. **A row spanning every account offers nothing**, because a folder is made, marked and removed inside one
// mailbox and a row standing for all of them stands for no place to put one. **Nesting stops at the third segment of
// an alias**, which is the column's own ceiling. **A level of an alias nothing is bound to is not a folder**, so no act
// that names one reaches it. And **marking everything read is a second grant**, asked of the credential rather than of
// the folder.

/** One thing a row of the folder tree offers, named by what it does rather than by the control that draws it. */
export type FolderAct = 'newFolder' | 'newFolderInside' | 'markAllRead' | 'editFolder' | 'deleteFolder';

/** What the service reported about the mailbox a row belongs to and about the row's own folder. */
export interface ReportedFolderActs {
    /** What the account itself allows, which is creating a folder or nothing. */
    readonly account: readonly ManagedMailFolderAct[];

    /** What this row's folder allows, or `null` where the report names no folder this row stands for. */
    readonly folder: readonly ManagedMailFolderAct[] | null;
}

/** A row about which the service said nothing, which offers no act that would reach the managed surface. */
export const nothingReported: ReportedFolderActs = { account: [], folder: null };

/**
 * What one row offers, in the order the design project draws them.
 *
 * @param row The row the gesture happened on.
 * @param reported What the service said the account and this row's folder allow.
 * @returns The acts, empty for a row that offers none — which is what says no menu opens at all.
 */
export function actsOffered(row: FolderTreeRow, reported: ReportedFolderActs): readonly FolderAct[] {
    // Every act names one mailbox, so the two rows that span them all — the whole workspace, and a role across it —
    // carry none. That is the design project's *All accounts carries none*, and it falls out of the row rather than
    // being asked about the row's kind.
    if (row.accountId === null) {
        return [];
    }

    const mayCreate = reported.account.includes('create');

    if (row.alias === null) {
        return [...(mayCreate ? (['newFolder'] as const) : []), 'markAllRead'];
    }

    // A folder can only be made beneath one the service named, since a creation names its parent by that folder's own
    // identity. A level of an alias nothing is bound to has none, so it offers nothing at all.
    const nestable = mayCreate && admitsNesting(row.alias) && reported.folder !== null;

    if (row.remotePath === null) {
        return nestable ? ['newFolderInside'] : [];
    }

    const allowed = reported.folder ?? [];

    return [
        ...(nestable ? (['newFolderInside'] as const) : []),
        'markAllRead',
        // Renaming and moving travel together on this surface and the dialog performs both, so one item is drawn for
        // the pair and it is drawn where either is allowed.
        ...(allowed.includes('rename') || allowed.includes('move') ? (['editFolder'] as const) : []),
        ...(allowed.includes('delete') ? (['deleteFolder'] as const) : []),
    ];
}
