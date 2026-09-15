// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFolderRole, ManagedMailFolder } from '@mailfathom/client-backend';
import { deepestAlias } from './folderTreeRows';

// What the folder dialog is holding while somebody types in it, and the arithmetic over it. It is a module rather than
// state inside the dialog because every interesting decision here is a function of a few values — and a decision that
// can be read as a value can be tested as one.
//
// **The dialog has two fields, and the second one is where the folder sits rather than where its mail server keeps
// it.** A folder used to be a mapping this client composed, so the design drew a remote path beside the name and the
// client published both. It is not one any more: the service owns where a folder lives on a mail server and never
// tells a client, so what is left to state is the name and the parent — which is also what the two acts behind the
// dialog are, a rename and a move. Publishing a server path from here would be this client deciding a mailbox's layout
// from a label, which is the one thing the managed surface exists to stop.
//
// **A folder playing a role is never named here.** Creating a missing one is choosing the role and nothing else: the
// service gives it the standard name, so the dialog hides the name field rather than proposing a name nobody may
// change afterwards — and editing one hides it for the same reason, the name on the screen being the service's rather
// than anybody's to retype. Where it sits stays its own question, which is why the role is carried on an edit too.

/** Which of the dialog's two acts is being performed, which decides its title, its button, and what a save composes. */
export type FolderDraftMode = 'create' | 'edit';

/** A folder a draft may sit beneath, named by the identity every act names it by rather than by any path. */
export interface FolderDraftParent {
    /** What the act names the folder by, opaque to this client. */
    readonly id: string;

    /** What the folder is called, which is what the field shows. */
    readonly name: string;
}

/** Everything the folder dialog is holding, which is what a save is composed from. */
export interface FolderDraft {
    readonly mode: FolderDraftMode;

    /** The account the folder belongs to, which never changes while the dialog is open. */
    readonly accountId: string;

    /** The mailbox's own name, which the dialog says the folder is being made in. */
    readonly accountName: string;

    /** The folder being edited, or `null` for one that does not exist yet. */
    readonly standingId: string | null;

    /** Where the folder would go, or `null` for the top of the mailbox. */
    readonly parentId: string | null;

    /** Where it sat when the dialog opened, which is what says whether saving has to move it. */
    readonly standingParentId: string | null;

    /** What is typed in the folder's name. */
    readonly name: string;

    /** What it was called when the dialog opened, which is what says whether saving has to rename it. */
    readonly standingName: string;

    /** The role the folder plays — asked for instead of a name on a creation, and read off the folder on an edit. */
    readonly role: MailFolderRole | null;
}

/** The draft a *New folder* on a mailbox or inside a folder opens with. */
export function draftForNewFolder(
    accountId: string,
    accountName: string,
    parent: FolderDraftParent | null,
): FolderDraft {
    return {
        mode: 'create',
        accountId,
        accountName,
        standingId: null,
        parentId: parent?.id ?? null,
        standingParentId: parent?.id ?? null,
        name: '',
        standingName: '',
        role: null,
    };
}

/** The draft *Edit folder* opens with, which is the folder as it stands. */
export function draftForEditedFolder(accountId: string, accountName: string, folder: ManagedMailFolder): FolderDraft {
    return {
        mode: 'edit',
        accountId,
        accountName,
        standingId: folder.id,
        parentId: folder.parentId,
        standingParentId: folder.parentId,
        name: folder.name,
        standingName: folder.name,
        role: folder.role,
    };
}

/** The draft one keystroke in the name leaves. */
export function withName(draft: FolderDraft, name: string): FolderDraft {
    return { ...draft, name };
}

/** The draft choosing another place in the hierarchy leaves, `null` being the top of the mailbox. */
export function withParent(draft: FolderDraft, parentId: string | null): FolderDraft {
    return { ...draft, parentId };
}

/**
 * The draft choosing to create a folder for a role leaves, or going back to an ordinary one.
 *
 * The name goes with the choice rather than being kept beside it: a role folder is named by the service, so a name
 * left over from before would be a value the dialog is holding and the request will not carry.
 */
export function withRole(draft: FolderDraft, role: MailFolderRole | null): FolderDraft {
    return { ...draft, role, name: role === null ? draft.name : '' };
}

/**
 * Why the draft cannot be saved yet, or `null` where it can.
 *
 * A reason rather than a boolean, because each is a different sentence the dialog owes somebody: an empty name is the
 * state the design draws the button flat for, a folder that would nest past the ceiling is the rule the tree is built
 * on, a name a sibling already carries is what the service refuses as `NameTaken`, and a folder moved inside itself
 * is the one shape of move that cannot mean anything. Each is said here so it is said before the request rather than
 * as a failure after it.
 */
export type FolderDraftRefusal = 'nameEmpty' | 'tooDeep' | 'nameTaken' | 'nestedInItself';

/**
 * Whether the draft may be saved, given the account's folders as the service last reported them.
 *
 * @param draft The draft.
 * @param folders Every folder the account has, which is what a collision and a depth are judged against.
 */
export function refusalOf(draft: FolderDraft, folders: readonly ManagedMailFolder[]): FolderDraftRefusal | null {
    // A folder *asked for* by role carries no name and no parent, so none of the three rules below has anything to say
    // about it: the service names it and puts it at the top of the hierarchy. One that already plays a role is a
    // different case — its name is fixed, but where it sits is being asked, and every rule about that still holds.
    if (draft.standingId === null && draft.role !== null) {
        return null;
    }

    const name = folderNameOf(draft);

    if (name.length === 0) {
        return 'nameEmpty';
    }

    if (draft.standingId !== null && beneath(draft.standingId, draft.parentId, folders)) {
        return 'nestedInItself';
    }

    // The ceiling is the tree's rather than a second decision: a draft that may be saved is one the column can draw.
    // Counted over what the folder brings with it, because a move takes everything beneath it along.
    if (depthOf(draft.parentId, folders) + 1 + heightOf(draft.standingId, folders) > deepestAlias) {
        return 'tooDeep';
    }

    const taken = folders.some(
        (folder) =>
            folder.parentId === draft.parentId &&
            folder.id !== draft.standingId &&
            folder.name.localeCompare(name, undefined, { sensitivity: 'accent' }) === 0,
    );

    return taken ? 'nameTaken' : null;
}

/** Whether saving the draft would leave the folder exactly as it stands, which is a save with nothing to ask for. */
export function unchanged(draft: FolderDraft): boolean {
    return (
        draft.standingId !== null &&
        draft.parentId === draft.standingParentId &&
        folderNameOf(draft) === draft.standingName
    );
}

/** The name a folder actually takes, which is what is typed with the surrounding space taken off. */
export function folderNameOf(draft: FolderDraft): string {
    return draft.name.trim();
}

/**
 * Where a folder sits, named level by level from the top of the mailbox.
 *
 * It is what the parent field shows, because a name alone does not say which of two folders called *2026* is meant —
 * and the account's hierarchy is the only thing this client may say about where a folder lives.
 */
export function namePathOf(folderId: string, folders: readonly ManagedMailFolder[]): string {
    const levels: string[] = [];
    let walking: string | null = folderId;

    while (walking !== null && levels.length <= folders.length) {
        const folder: ManagedMailFolder | undefined = folders.find((entry) => entry.id === walking);

        if (folder === undefined) {
            break;
        }

        levels.unshift(folder.name);
        walking = folder.parentId;
    }

    return levels.join(' / ');
}

/**
 * The folders the draft may be placed beneath, which is what the parent field offers beside the top of the mailbox.
 *
 * Three are left out, and each of them is a refusal that would otherwise be spent on a round trip: the folder itself
 * and everything under it, which is a hierarchy eating itself; anything that would leave the folder and its own
 * subfolders past the column's ceiling; and anything the service reported as taking no new folder inside it.
 */
export function admissibleParents(
    draft: FolderDraft,
    folders: readonly ManagedMailFolder[],
): readonly ManagedMailFolder[] {
    const height = heightOf(draft.standingId, folders);

    return folders.filter(
        (folder) =>
            folder.id !== draft.standingId &&
            (draft.standingId === null || !beneath(draft.standingId, folder.id, folders)) &&
            depthOf(folder.id, folders) + 1 + height <= deepestAlias,
    );
}

/** How deep a folder sits, counted from one, and nought for the top of the mailbox. */
export function depthOf(folderId: string | null, folders: readonly ManagedMailFolder[]): number {
    let depth = 0;
    let walking = folderId;

    // Bounded by the list rather than by the hierarchy's own shape: a report whose parents formed a cycle would
    // otherwise be an answer this client walks forever, and a hierarchy is never deeper than it has folders.
    while (walking !== null && depth <= folders.length) {
        const parent: ManagedMailFolder | undefined = folders.find((folder) => folder.id === walking);

        if (parent === undefined) {
            return depth;
        }

        depth += 1;
        walking = parent.parentId;
    }

    return depth;
}

// How many levels hang below a folder, nought for one with nothing beneath it. It is what a move has to count: the
// folder travels with everything under it, so a branch two deep cannot go anywhere that leaves it past the ceiling.
function heightOf(folderId: string | null, folders: readonly ManagedMailFolder[]): number {
    if (folderId === null) {
        return 0;
    }

    const children = folders.filter((folder) => folder.parentId === folderId);

    return children.length === 0 ? 0 : 1 + Math.max(...children.map((child) => heightOf(child.id, folders)));
}

// Whether one folder sits under another, itself included, which is the move a hierarchy cannot take.
function beneath(folderId: string, parentId: string | null, folders: readonly ManagedMailFolder[]): boolean {
    let walking = parentId;

    for (let step = 0; walking !== null && step <= folders.length; step += 1) {
        if (walking === folderId) {
            return true;
        }

        walking = folders.find((folder) => folder.id === walking)?.parentId ?? null;
    }

    return false;
}
