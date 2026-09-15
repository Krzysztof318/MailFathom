// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { isMailFolderRole, type MailFolderRole } from './mailFolders';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';
// What a person does to the folders themselves — makes one, renames one, moves one, removes one — as against
// `mailFolders.ts`, which answers what the folders *are* and how much mail is in each. Two readings of one mailbox,
// and they are two modules because they are two routes on the service for a reason this package must not undo.
//
// **Nothing here says where an account's mail is kept, and nothing here may be made to.** The service decides whether
// an act runs against a mail server or against the hierarchy MailFathom holds, and what it publishes instead is the
// acts each account and each folder currently allow. So a screen draws its menus from `allowedActs` and never from a
// rule of its own: a client that worked one out would be wrong about the first account whose storage changed under it.
//
// **A folder is named by an identity whose shape is not a contract.** It is read, kept, and handed back, and a rename
// or a move never invalidates one. Nothing in this package parses it, compares it to a name, or builds one.
//
// **A refused act is a value rather than a failure**, for the reason `mailDrafts.ts` gives about a refused send: a name
// a sibling already carries and a deployment that is down are different sentences with different remedies, and a
// caller that could not tell them apart would offer a retry for the one and nothing for the other.

/** The route an account's folders and their acts are read at, and one folder created at, relative to the client prefix. */
export const managedMailFoldersRoute = '/managed-folders';

/** The route one folder is renamed at. */
export const managedMailFolderRenamesRoute = `${managedMailFoldersRoute}/renames`;

/** The route one folder is moved at. */
export const managedMailFolderMovesRoute = `${managedMailFoldersRoute}/moves`;

/** The route one folder is deleted at. */
export const managedMailFolderDeletionsRoute = `${managedMailFoldersRoute}/deletions`;

/** One of the four things a person may ask of a folder, which the service reports rather than a client deciding. */
export type ManagedMailFolderAct = 'create' | 'rename' | 'move' | 'delete';

/**
 * What an act turned out to mean, which is not always what it asked for.
 *
 * Deleting is where the two come apart: it moves the folder into the trash, erases it where it was already there,
 * deletes it on the mail server, or marks it deleted here while the server keeps it — and which of the four happened
 * is the only thing that says what became of the mail. A screen that reported *deleted* for all of them would be
 * telling somebody their mail is gone when it is not, and the reverse.
 */
export type ManagedMailFolderChange =
    'created' | 'renamed' | 'moved' | 'movedToTrash' | 'erased' | 'deleted' | 'markedDeleted';

/**
 * Why an act was refused, in this package's own words.
 *
 * Every one of them names something different to do about it — choose another name, go no deeper, ask whoever
 * administers the deployment, wait for a restore to end, try again — which is why they are not collapsed into a
 * failure. `refusedForAnotherReason` is the member that keeps a deployment ahead of this client readable: a refusal
 * this package does not know is still a refusal rather than a body it could not read.
 */
export type ManagedMailFolderRefusal =
    | 'accountMissing'
    | 'accountRestoring'
    | 'folderMissing'
    | 'parentMissing'
    | 'protectedRole'
    | 'nameInvalid'
    | 'inboxNameAtTopLevel'
    | 'nameTaken'
    | 'nestedInItself'
    | 'tooDeep'
    | 'tooManyFolders'
    | 'serverRefused'
    | 'serverUnavailable'
    | 'roleAlreadyPlayed'
    | 'notDeclaredByTheAccount'
    | 'notRecorded'
    | 'refusedForAnotherReason';

/** One folder of an account as the surface that manages folders reads it. */
export interface ManagedMailFolder {
    /** What every act names the folder by, opaque to whoever holds it. */
    readonly id: string;

    /** The folder it sits beneath, or `null` at the top of the hierarchy. */
    readonly parentId: string | null;

    /** The folder's own level of the hierarchy, which is what a person sees. */
    readonly name: string;

    /** The role the folder plays for its account, or `null` where it plays none. */
    readonly role: MailFolderRole | null;

    /** Which acts this folder currently allows, empty where it allows none. */
    readonly allowedActs: readonly ManagedMailFolderAct[];
}

/** What one account's folders are and what may be done to them. */
export interface ManagedMailFolders {
    /** What the account itself allows, which is `create` or nothing. */
    readonly allowedActs: readonly ManagedMailFolderAct[];

    /** The roles the account has no folder for, each of which a creation may name instead of a name. */
    readonly creatableRoles: readonly MailFolderRole[];

    /** The account's folders, ordered by name, each with the acts it allows. */
    readonly folders: readonly ManagedMailFolder[];
}

/** What one act came to: the change it made, or the rule that refused it. */
export type ManagedMailFolderOutcome =
    | {
          readonly committed: true;
          readonly change: ManagedMailFolderChange;
          readonly folder: ManagedMailFolder;

          /**
           * Whether removing the folder's stored mail found the job queue full, so it waits for the account's next erasure.
           *
           * The folder is gone either way. What this says is that the mail it held is stored and out of every listing
           * for a while longer, which is a different sentence from the one an act without it earns.
           */
          readonly mailErasureDeferred: boolean;
      }
    | { readonly committed: false; readonly refusal: ManagedMailFolderRefusal };

// The names the service answers with, beside what each is called here. Matched rather than passed through, so the
// vocabulary a screen switches on is this package's and a rename on the service is one line here rather than a screen
// silently stopping to match.
const refusalsByName: Readonly<Record<string, ManagedMailFolderRefusal>> = {
    AccountMissing: 'accountMissing',
    AccountNotHeld: 'accountRestoring',
    FolderMissing: 'folderMissing',
    ParentMissing: 'parentMissing',
    ProtectedRole: 'protectedRole',
    NameInvalid: 'nameInvalid',
    InboxNameAtTopLevel: 'inboxNameAtTopLevel',
    NameTaken: 'nameTaken',
    NestedInItself: 'nestedInItself',
    TooDeep: 'tooDeep',
    TooManyFolders: 'tooManyFolders',
    ServerRefused: 'serverRefused',
    ServerUnavailable: 'serverUnavailable',
    RoleAlreadyPlayed: 'roleAlreadyPlayed',
    NotDeclaredByTheAccount: 'notDeclaredByTheAccount',
    NotRecorded: 'notRecorded',
};

const actsByName: Readonly<Record<string, ManagedMailFolderAct>> = {
    Create: 'create',
    Rename: 'rename',
    Move: 'move',
    Delete: 'delete',
};

const changesByName: Readonly<Record<string, ManagedMailFolderChange>> = {
    Created: 'created',
    Renamed: 'renamed',
    Moved: 'moved',
    MovedToTrash: 'movedToTrash',
    Erased: 'erased',
    Deleted: 'deleted',
    MarkedDeleted: 'markedDeleted',
};

// What one account may answer with before the report is refused unread, and the same bound the folder tree applies
// for the same reason: it is far above any hierarchy a mailbox has and exists for the answer that is not one. Checked
// during the walk rather than after it, because a bound applied afterwards is not a bound.
const maximumFoldersInAccount = 1_024;

// A write answers one folder and what became of it, and a read answers a hierarchy of them. Both are bounded before
// the body is received rather than after, so a deployment answering something enormous is refused rather than read.
const longestWriteAnswer = 32 * 1024;
const longestReadAnswer = 512 * 1024;

/** Reads one account's folders and which acts it and each of them allow. */
export function readManagedMailFolders(
    session: ClientSession,
    transport: MailFathomTransport,
    account: string,
): Promise<ClientResult<ManagedMailFolders>> {
    return spanned(`GET ${managedMailFoldersRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, managedMailFoldersRoute)}?account=${encodeURIComponent(account)}`,
            headers: headersFor(session),
            longestAnswer: longestReadAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const folders = parseManagement(response.body);

        return folders === null ? failed('unreadable', response.status) : read(folders);
    });
}

/**
 * Makes a folder, either by naming it or by naming the role it is to play.
 *
 * @param stated The account, and either a name with the folder to make it beneath, or a role instead of both.
 */
export function createManagedMailFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    stated:
        | { readonly account: string; readonly parentId: string | null; readonly name: string }
        | { readonly account: string; readonly role: MailFolderRole },
): Promise<ClientResult<ManagedMailFolderOutcome>> {
    // A creation naming a role names neither a name nor a parent: the service gives such a folder the role's standard
    // name at the top of the hierarchy, so nobody is ever asked to name their own trash folder.
    return act(
        session,
        transport,
        managedMailFoldersRoute,
        'role' in stated
            ? { account: stated.account, role: stated.role }
            : { account: stated.account, parentId: stated.parentId, name: stated.name },
    );
}

/** Gives a folder another name, leaving it where it is. */
export function renameManagedMailFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: { readonly account: string; readonly folderId: string; readonly name: string },
): Promise<ClientResult<ManagedMailFolderOutcome>> {
    return act(session, transport, managedMailFolderRenamesRoute, stated);
}

/** Puts a folder, with everything beneath it, somewhere else in the hierarchy — `null` being the top of it. */
export function moveManagedMailFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: { readonly account: string; readonly folderId: string; readonly parentId: string | null },
): Promise<ClientResult<ManagedMailFolderOutcome>> {
    return act(session, transport, managedMailFolderMovesRoute, stated);
}

/** Deletes a folder, which the answer says what it meant for the folder's mail. */
export function deleteManagedMailFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: { readonly account: string; readonly folderId: string },
): Promise<ClientResult<ManagedMailFolderOutcome>> {
    return act(session, transport, managedMailFolderDeletionsRoute, stated);
}

function act(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    stated: object,
): Promise<ClientResult<ManagedMailFolderOutcome>> {
    return spanned(`POST ${route}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, route),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify(stated),
            longestAnswer: longestWriteAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 200) {
            const outcome = parseAct(response.body);

            return outcome === null ? failed('unreadable', response.status) : read(outcome);
        }

        // A credential that is refused or not granted this act is a failure rather than a rule about the folder: there
        // is nothing to say about the name somebody typed, and what the screen does about it is sign in again.
        if (response.status === 401 || response.status === 403) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        // An answer this client cannot read as a refusal is read as the failure its status names, which is what a
        // `503` carrying no body at all is: something in front of the deployment rather than a rule of it.
        const refused = refusalIn(response);

        return refused === null
            ? failed(failureReasonForStatus(response.status), response.status)
            : read({ committed: false, refusal: refused });
    });
}

function refusalIn(response: ClientResponse): ManagedMailFolderRefusal | null {
    const body = parsed(response.body);

    if (body === null) {
        return null;
    }

    const named = body['refusal'];

    return typeof named === 'string' ? (refusalsByName[named] ?? 'refusedForAnotherReason') : null;
}

function parseAct(body: string): ManagedMailFolderOutcome | null {
    const record = parsed(body);

    if (record === null) {
        return null;
    }

    const named = record['change'];
    const change = typeof named === 'string' ? changesByName[named] : undefined;
    const folder = parseFolder(record['folder']);
    const deferred = record['mailErasureDeferred'];

    // A change this client does not know is refused rather than reported as something else. There are seven, each
    // saying something different about the mail, so reading an eighth as any of them would put the wrong sentence on
    // the screen about mail somebody may not get back.
    return change === undefined || folder === null || typeof deferred !== 'boolean'
        ? null
        : { committed: true, change, folder, mailErasureDeferred: deferred };
}

function parseManagement(body: string): ManagedMailFolders | null {
    const record = parsed(body);

    if (record === null) {
        return null;
    }

    const allowedActs = parseActs(record['allowedActs']);
    const creatableRoles = parseRoles(record['creatableRoles']);
    const stated = record['folders'];

    if (allowedActs === null || creatableRoles === null || !Array.isArray(stated)) {
        return null;
    }

    const folders: ManagedMailFolder[] = [];
    for (const entry of stated) {
        const folder = parseFolder(entry);

        if (folder === null || folders.length >= maximumFoldersInAccount) {
            return null;
        }

        folders.push(folder);
    }

    return { allowedActs, creatableRoles, folders };
}

function parseFolder(value: unknown): ManagedMailFolder | null {
    const record = asRecord(value);

    if (record === null) {
        return null;
    }

    const id = record['id'];
    const parentId = record['parentId'] ?? null;
    const name = record['name'];
    const role = record['role'] ?? null;
    const allowedActs = parseActs(record['allowedActs']);

    if (typeof id !== 'string' || id.length === 0 || typeof name !== 'string' || allowedActs === null) {
        return null;
    }

    if (parentId !== null && (typeof parentId !== 'string' || parentId.length === 0)) {
        return null;
    }

    if (role !== null && !isMailFolderRole(role)) {
        return null;
    }

    return { id, parentId, name, role, allowedActs };
}

function parseActs(value: unknown): readonly ManagedMailFolderAct[] | null {
    if (!Array.isArray(value)) {
        return null;
    }

    const acts: ManagedMailFolderAct[] = [];
    for (const stated of value) {
        const named = typeof stated === 'string' ? actsByName[stated] : undefined;

        // An act this client does not know is an answer from a deployment ahead of it, and the safe reading is to
        // leave it out: a menu drawing one it cannot perform is worse than a menu missing one it could.
        if (named !== undefined && !acts.includes(named)) {
            acts.push(named);
        }
    }

    return acts;
}

function parseRoles(value: unknown): readonly MailFolderRole[] | null {
    if (!Array.isArray(value)) {
        return null;
    }

    const roles: MailFolderRole[] = [];
    for (const stated of value) {
        if (isMailFolderRole(stated) && !roles.includes(stated)) {
            roles.push(stated);
        }
    }

    return roles;
}

function parsed(body: string): Readonly<Record<string, unknown>> | null {
    try {
        return asRecord(JSON.parse(body));
    } catch {
        return null;
    }
}
