// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { FolderAct } from './folderActs';

// What a row of the folder column answers a press with. It is this row's items and nothing else: where the menu
// stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s, which is the same component six
// other lists open.
//
// Which items a row offers is `folderActs.ts` read against the grant, and both of those are the column's — a mailbox
// heading offers making a folder and marking everything read, a folder offers those plus editing and removing itself
// where it plays no role, and a row standing for every mailbox at once offers nothing at all. This module is the
// symbol and the word each of them is drawn with, which is a rendering decision and therefore neither of those.

const actIcons: Readonly<Record<FolderAct, IconName>> = {
    newFolder: 'create_new_folder',
    newFolderInside: 'create_new_folder',
    markAllRead: 'mark_email_read',
    editFolder: 'edit',
    deleteFolder: 'delete',
};

const actLabels: Readonly<Record<FolderAct, MessageKey>> = {
    newFolder: 'folders.newFolder',
    newFolderInside: 'folders.newFolderInside',
    markAllRead: 'folders.markAllRead',
    editFolder: 'folders.editFolder',
    deleteFolder: 'folders.deleteFolder',
};

export function FolderRowMenu({
    acts,
    header,
    at,
    onAct,
    onClose,
}: {
    /** What this row offers, in the order the design project draws them. */
    readonly acts: readonly FolderAct[];

    /** What the menu is about, in the row's own words, which is the name the menu carries. */
    readonly header: string;

    /** Where the gesture happened, in the window's own coordinates. */
    readonly at: MenuPoint;

    readonly onAct: (act: FolderAct) => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();

    const items: readonly ContextMenuItem[] = acts.map((act) => ({
        icon: actIcons[act],
        label: translate(actLabels[act]),
        destroys: act === 'deleteFolder',
        choose: () => {
            onAct(act);
        },
    }));

    return <ContextMenu header={header} at={at} items={items} onClose={onClose} />;
}
