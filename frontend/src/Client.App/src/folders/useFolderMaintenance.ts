// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFolderRole, ManagedMailFolder } from '@mailfathom/client-backend';
import { createContext, useContext } from 'react';
import type { FolderDraftParent } from './folderDraft';

// What a person does to their own folders, as operations rather than as anything on a screen. It belongs to the
// application rather than to the folder column because two unrelated surfaces reach the same acts — the column's own
// row menus, and *New folder here* in the sheet that files mail — and a second dialog for making a folder is how a
// client comes to have two ideas of what a folder is.
//
// **These acts change the mailbox itself, and the service decides what that means.** Making one makes a folder,
// renaming one renames it, moving one takes everything beneath it along, and removing one removes it and may take its
// mail with it. Whether any of that reaches a mail server or the hierarchy MailFathom holds is not a question this
// client asks or may answer: what it is given is the acts each folder allows, and what it is told afterwards is which
// change the act turned out to be. That is why nothing here is named for a mapping or a declaration any more.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

/** The mailbox an act is inside, as the service last reported it. */
export interface FolderMailbox {
    readonly accountId: string;

    /** What the mailbox is called, which the dialog says the folder is being made in. */
    readonly accountName: string;

    /**
     * Every folder the service named for the mailbox, with the acts each allows.
     *
     * Handed over rather than read here, because the caller already holds it: a second read of the account's folders
     * would be a request every menu press pays for so that the dialog can answer questions — which parent, which
     * siblings, how deep — its opener has already answered.
     */
    readonly folders: readonly ManagedMailFolder[];

    /** The roles the mailbox still has no folder for, each of which a creation may ask for instead of a name. */
    readonly creatableRoles: readonly MailFolderRole[];
}

/** A folder about to be removed, in the words the question in front of it needs. */
export interface RemovedFolder {
    /** What the act names the folder by. */
    readonly id: string;

    /** What the folder is called on the screen, which is what the question names. */
    readonly name: string;

    /** Whether the mailbox has a folder nested inside this one, which the question says goes with it. */
    readonly holdsNested: boolean;
}

export interface FolderMaintenance {
    /**
     * Whether this credential may change the mailbox's folders at all, which is what draws the acts.
     *
     * An act a credential may not take is absent rather than drawn and refused, which is the rule `shell/capabilities.ts`
     * states — and the strip at the top of the frame is where the absence is explained.
     */
    readonly offered: boolean;

    /**
     * Whether this credential may write the read flag, which is the grant *Mark all as read* is reached under.
     *
     * A second flag beside the one above because the two acts are two grants: somebody who may mark mail read need
     * not be somebody who may change the folders, and a menu drawing both off one answer would offer an act the route
     * behind it turns away.
     */
    readonly marksRead: boolean;

    /** How many folder changes have committed, which is what says a tree read minutes ago is worth reading again. */
    readonly changed: number;

    /** Opens the dialog on a folder that does not exist yet, inside `parent` or at the top of the mailbox. */
    readonly declare: (mailbox: FolderMailbox, parent: FolderDraftParent | null) => void;

    /** Opens the same dialog on a folder that does, which is where it is renamed and where it is moved. */
    readonly revise: (mailbox: FolderMailbox, folder: ManagedMailFolder) => void;

    /** Asks whether to remove a folder, and removes it where somebody answers yes. */
    readonly remove: (mailbox: FolderMailbox, folder: RemovedFolder) => void;

    /**
     * Marks everything unread in one folder read, or in the whole mailbox where no folder is named.
     *
     * @param said What the folder or mailbox is called, which is what the toasts about it name.
     */
    readonly markAllRead: (accountId: string, folder: string | null, said: string) => void;
}

/** What a tree with no provider above it reads, which is a client that offers none of these acts. */
export const noFolderMaintenance: FolderMaintenance = {
    offered: false,
    marksRead: false,
    changed: 0,
    declare: () => undefined,
    revise: () => undefined,
    remove: () => undefined,
    markAllRead: () => undefined,
};

export const FolderMaintenanceContext = createContext<FolderMaintenance>(noFolderMaintenance);

export function useFolderMaintenance(): FolderMaintenance {
    return useContext(FolderMaintenanceContext);
}
