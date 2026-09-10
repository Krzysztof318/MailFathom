// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { DeclaredFolder, FolderDraftParent } from './folderDraft';

// What a person does to their own folders, as operations rather than as anything on a screen. It belongs to the
// application rather than to the folder column because two unrelated surfaces reach the same acts — the column's own
// row menus, and *New folder here* in the sheet that files mail — and a second dialog for making a folder is how a
// client comes to have two ideas of what a folder is.
//
// **Nothing here reaches a mail server, and that is the whole shape of these acts.** A folder is a mapping in the
// person's own record: making one adds the mapping and lets the account's next run create the folder its
// configuration names, editing one states the mapping afresh, and removing one withdraws the mapping and leaves both
// the mail already stored and the folder on the server where they are. ADR 0007 is why — renaming and deleting a
// folder on somebody's mail server are refused outright — so the words every surface uses are the record's rather
// than the mailbox's.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

/** The mailbox an act is inside, which is the account, its name, and what a name collision is judged against. */
export interface FolderMailbox {
    readonly accountId: string;

    /** What the mailbox is called, which the dialog says the folder is being made in. */
    readonly accountName: string;

    /**
     * Every alias the mailbox already declares.
     *
     * Handed over rather than read here, because both callers already hold it: a second read of the folders would be
     * a request every session pays for so that the dialog can answer a question its opener could have answered.
     */
    readonly declaredAliases: readonly string[];
}

/** A folder about to stop being read, in the words the question in front of it needs. */
export interface WithdrawnFolder extends DeclaredFolder {
    /** What the folder is called on the screen, which is its own last level rather than its alias. */
    readonly name: string;

    /** Whether the mailbox declares a folder nested inside this one, which the question says stays behind. */
    readonly holdsNested: boolean;
}

export interface FolderMaintenance {
    /**
     * Whether this credential may change what the deployment reads at all, which is what draws the acts.
     *
     * An act a credential may not take is absent rather than drawn and refused, which is the rule `shell/capabilities.ts`
     * states — and the strip at the top of the frame is where the absence is explained.
     */
    readonly offered: boolean;

    /**
     * Whether this credential may write the read flag, which is the grant *Mark all as read* is reached under.
     *
     * A second flag beside the one above because the two acts are two grants: somebody who may mark mail read need
     * not be somebody who may change what the deployment reads, and a menu drawing both off one answer would offer an
     * act the route behind it turns away.
     */
    readonly marksRead: boolean;

    /** How many folder changes have committed, which is what says a tree read minutes ago is worth reading again. */
    readonly changed: number;

    /** Opens the dialog on a folder that does not exist yet, inside `parent` or at the top of the mailbox. */
    readonly declare: (mailbox: FolderMailbox, parent: FolderDraftParent | null) => void;

    /** Opens the same dialog on a folder that does, which is the one act that may move where mail is read from. */
    readonly revise: (mailbox: FolderMailbox, folder: DeclaredFolder, parent: FolderDraftParent | null) => void;

    /** Asks whether to stop reading a folder, and stops reading it where somebody answers yes. */
    readonly withdraw: (mailbox: FolderMailbox, folder: WithdrawnFolder) => void;

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
    withdraw: () => undefined,
    markAllRead: () => undefined,
};

export const FolderMaintenanceContext = createContext<FolderMaintenance>(noFolderMaintenance);

export function useFolderMaintenance(): FolderMaintenance {
    return useContext(FolderMaintenanceContext);
}
