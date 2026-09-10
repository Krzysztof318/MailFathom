// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { aliasSeparator, aliasSegments, deepestAlias } from './folderTreeRows';

// What the folder dialog is holding while somebody types in it, and the arithmetic over it. It is a module rather than
// state inside the dialog because every interesting decision here is a function of two strings — and a decision that
// can be read as a value can be tested as one, which is what the two fields following each other until they stop
// needs most.
//
// **The two fields are two different things, and the design project says so by drawing both.** The name is what the
// folder is called here, and the whole of it — the parent's name and this one, joined — is the alias every route on
// the client surface names the folder by. The remote path is where the folder sits on somebody's mail server, which
// is a second hierarchy a provider, a delimiter and a language each get a say in. A client that published one as the
// other would be deciding a mailbox's layout from a label.
//
// **The path follows the name until it is typed in, and then stops.** That is the design's own rule and it is the
// whole of why the draft carries a third value: without it, either the path never fills itself in, or a path somebody
// wrote by hand is overwritten by the next letter they add to the name.

/** How a remote path is spelled in the one field that shows one, which is not necessarily the server's own delimiter. */
export const remotePathSeparator = '/';

/** Where the dialog starts a path from when the folder it is making has no parent to take one from. */
export const rootRemotePath = 'INBOX';

/** Which of the dialog's two acts is being performed, which decides its title, its button, and what a save composes. */
export type FolderDraftMode = 'create' | 'edit';

/** The folder a draft is being composed inside or over, or `null` for one being made at the top of a mailbox. */
export interface FolderDraftParent {
    /** The alias the parent is named by, whole. */
    readonly alias: string;

    /** Where the parent sits on its mail server, which is what a child's path is proposed from. */
    readonly remotePath: readonly string[];
}

/** Everything the folder dialog is holding, which is what a save is composed from. */
export interface FolderDraft {
    readonly mode: FolderDraftMode;

    /** The account the folder belongs to, which never changes while the dialog is open. */
    readonly accountId: string;

    /** The mailbox's own name, which the dialog says the folder is being made in. */
    readonly accountName: string;

    /** The folder this one is being made inside, or `null` at the top of the mailbox. */
    readonly parent: FolderDraftParent | null;

    /** The alias the folder is declared under now, or `null` for one that does not exist yet. */
    readonly standingAlias: string | null;

    /** What is typed in the folder's name. */
    readonly name: string;

    /** What is typed in the folder's remote path. */
    readonly remotePath: string;

    /** Whether the path has been typed in by hand, after which it stops following the name. */
    readonly pathWritten: boolean;
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
        parent,
        standingAlias: null,
        name: '',
        remotePath: '',
        pathWritten: false,
    };
}

/** A folder as it is declared today, which is what an edit opens on and what a removal names. */
export interface DeclaredFolder {
    /** The alias the folder is declared under, whole. */
    readonly alias: string;

    /** Where it sits on its mail server, outermost level first. */
    readonly remotePath: readonly string[];
}

/** The draft *Edit folder* opens with, which is the folder as it stands. */
export function draftForEditedFolder(
    accountId: string,
    accountName: string,
    folder: DeclaredFolder,
    parent: FolderDraftParent | null,
): FolderDraft {
    return {
        mode: 'edit',
        accountId,
        accountName,
        parent,
        standingAlias: folder.alias,
        name: aliasSegments(folder.alias).at(-1) ?? folder.alias,
        // Already written, because it is a path this folder actually has: letting it follow the name would rewrite
        // where somebody's mail lives the moment they corrected a spelling.
        remotePath: folder.remotePath.join(remotePathSeparator),
        pathWritten: true,
    };
}

/** The draft one keystroke in the name leaves, with the path following it where it is still following. */
export function withName(draft: FolderDraft, name: string): FolderDraft {
    return {
        ...draft,
        name,
        remotePath: draft.pathWritten ? draft.remotePath : proposedRemotePath(draft, name),
    };
}

/** The draft one keystroke in the path leaves, after which the path is the person's rather than the name's. */
export function withRemotePath(draft: FolderDraft, remotePath: string): FolderDraft {
    return { ...draft, remotePath, pathWritten: true };
}

/**
 * Where on the server the folder would go, which is what the path field's own example is written under.
 *
 * It ends in a separator because what follows it is an example name the catalogue holds: the field shows a whole
 * path a person could have typed rather than a stem trailing off, which is the only way the example says anything
 * about the shape of the value.
 */
export function remotePathBase(draft: FolderDraft): string {
    return `${remoteBaseOf(draft)}${remotePathSeparator}`;
}

/** The alias the draft would leave, which is the parent's and this folder's name joined. */
export function draftAlias(draft: FolderDraft): string {
    const own = folderNameOf(draft);

    return draft.parent === null ? own : `${draft.parent.alias}${aliasSeparator}${own}`;
}

/** Where the draft would put the folder on its mail server, outermost level first. */
export function draftRemotePath(draft: FolderDraft): readonly string[] {
    const written = draft.pathWritten ? draft.remotePath : proposedRemotePath(draft, draft.name);

    return written
        .split(remotePathSeparator)
        .map((level) => level.trim())
        .filter((level) => level.length > 0);
}

/**
 * Why the draft cannot be saved yet, or `null` where it can.
 *
 * A reason rather than a boolean, because each of the three is a different sentence the dialog owes somebody: an
 * empty name is the state the design draws the button flat for, a name that would nest past the ceiling is the rule
 * the tree is built on, and an alias the mailbox already declares is a collision the deployment would refuse — said
 * here so that it is said before the request rather than as a failure afterwards.
 */
export type FolderDraftRefusal = 'nameEmpty' | 'tooDeep' | 'aliasTaken' | 'remotePathEmpty';

/**
 * Whether the draft may be saved, given the aliases the mailbox already declares.
 *
 * @param draft The draft.
 * @param declared Every alias the account holds, which is what a collision is judged against.
 */
export function refusalOf(draft: FolderDraft, declared: Iterable<string>): FolderDraftRefusal | null {
    if (folderNameOf(draft).length === 0) {
        return 'nameEmpty';
    }

    const alias = draftAlias(draft);

    // The ceiling is the tree's rather than a second decision: a draft that may be saved is one it can draw.
    if (aliasSegments(alias).length > deepestAlias) {
        return 'tooDeep';
    }

    if (draftRemotePath(draft).length === 0) {
        return 'remotePathEmpty';
    }

    const taken = [...declared].some(
        (existing) =>
            existing.localeCompare(alias, undefined, { sensitivity: 'accent' }) === 0 &&
            existing.localeCompare(draft.standingAlias ?? '', undefined, { sensitivity: 'accent' }) !== 0,
    );

    return taken ? 'aliasTaken' : null;
}

// The name a folder actually takes, which is what is typed with the separator taken out of it. A slash there would
// make one folder read as two levels of the tree, so it becomes a space rather than being refused — somebody typing
// `Q1/Q2` meant one name, and a dialog that stopped them mid-word to say so would be answering a question they did
// not ask.
function folderNameOf(draft: FolderDraft): string {
    return draft.name.split(aliasSeparator).join(' ').trim();
}

// Where a folder made here would sit, before anybody types a path: under the parent's own place on the server, or at
// the root of the mailbox where there is no parent. The parent's real path rather than its alias, because the alias
// is MailFathom's name for the folder and the server's hierarchy is what a path has to be composed against.
function remoteBaseOf(draft: FolderDraft): string {
    const above = draft.parent?.remotePath ?? [];

    return above.length > 0 ? above.join(remotePathSeparator) : rootRemotePath;
}

function proposedRemotePath(draft: FolderDraft, name: string): string {
    const own = name.split(aliasSeparator).join(' ').trim();

    return own.length === 0 ? '' : `${remoteBaseOf(draft)}${remotePathSeparator}${own}`;
}
