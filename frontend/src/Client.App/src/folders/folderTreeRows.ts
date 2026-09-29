// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAccountFolders, MailFolder, MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import { everything, roleRank, rolesAcrossAccounts, scopeKey, type MailScope } from '../workspace/mailScope';

// What the service answered, turned into the rows a tree draws. It is a function over values rather than anything a
// component does while rendering: the shape of the tree is the interesting decision here, and a decision that can be
// read as a value can be tested as one.
//
// Four things it decides. The user's mailboxes are one workspace rather than four applications, so the tree opens with
// every account at once and the roles that span them — the inbox of all three accounts is a thing somebody wants as
// often as the inbox of one. That group is what several mailboxes are for, so a user holding exactly one account is
// not offered it: it would draw that account's folders a second time under a heading meaning the same thing. Below it
// each account carries its own folders, nested by where each of them sits. And a folder that plays a role is placed by
// that role rather than by its name, because a name is whatever a provider chose in whatever language.
//
// **The nesting is the path's and not the alias's.** The path is where the folder sits — the levels the service
// answered, outermost first, which is what a folder act moves and what a reload reads back. An alias is MailFathom's own
// name for a folder and is what every route names it by, but it says nothing about where the folder is: a folder made
// inside another is declared under its own name, and neither a rename nor a move changes its alias, so a tree nested by
// alias would draw every folder somebody filed as if it sat at the top. The alias is what a row is keyed and acted on
// by, whatever an account's folders happen to be called, and it decides nothing about the shape of the tree.

/** How many levels a mailbox nests, counted from its top: `Inbox/Projects/2026` and no further. */
export const deepestFolder = 3;

/** One row of the tree, whatever it stands for: the whole workspace, a role across it, an account, or a folder. */
export interface FolderTreeRow {
    /** What identifies the row — what is folded, what is focused, and what is compared against the current scope. */
    readonly key: string;

    /**
     * What selecting the row scopes the client to, or `null` for a level of a path the service named no folder at.
     *
     * Not the same thing as the key, and an account's row is where the two part: pressing a mailbox's name means its
     * inbox rather than every folder it has at once, so the row is keyed by the account and scopes to the inbox. What
     * that leaves is a heading that selects without ever drawing as the selected row, which is how the design draws a
     * group: the row that lights up is the inbox beneath it, and that is the row somebody was pointing at.
     */
    readonly scope: MailScope | null;

    /** The name whatever this row stands for has: a mailbox's display name, a level of a path, or a folder's own. */
    readonly name: string;

    /** The role this row stands for, where it has one — in which case the role names it rather than `name` does. */
    readonly role: MailFolderRole | null;

    /**
     * The account this row belongs to, or `null` for the two rows that span every account.
     *
     * It is what the menus on a row are drawn from rather than anything the tree itself reads: a folder is created,
     * edited and removed inside one mailbox, so a row standing for every mailbox at once carries no such act — which
     * is the design's *All accounts carries none* stated as a value rather than as a case in a component.
     */
    readonly accountId: string | null;

    /** The alias this row stands for, whole, or `null` for a row standing for no one folder of one account. */
    readonly alias: string | null;

    /** The folder's place on its mail server, or `null` where this row stands for no folder the service named. */
    readonly remotePath: readonly string[] | null;

    /** How deep the row sits, counted from one, which is what a tree reports as its level. */
    readonly level: number;

    /**
     * Whether the row is drawn folded away until somebody opens it.
     *
     * The folders a mailbox nests open collapsed, which is the design's own reading and the only one that scales: a
     * mailbox filed three levels deep would otherwise open as a list of everything it has ever held. An account opens
     * expanded, because a column showing no folder at all is a column saying nothing.
     */
    readonly opensCollapsed: boolean;

    /** How many unread messages the deployment holds here, or `null` where nothing counted any. */
    readonly unreadEmailCount: number | null;

    /** How many messages in total the deployment holds here, or `null` where nothing counted any. */
    readonly storedEmailCount: number | null;

    readonly children: readonly FolderTreeRow[];
}

/** One row as it is drawn, with what a tree has to say about where it sits among the rows a reader can see. */
export interface VisibleRow {
    readonly row: FolderTreeRow;

    /** Where the row falls among its siblings, counted from one. */
    readonly position: number;

    /** How many siblings it has, itself included. */
    readonly setSize: number;

    /** Whether it is open, or `null` where it has nothing to open. */
    readonly expanded: boolean | null;
}

// A level of a path while the tree is being built: the folder sitting at exactly that path where there is one, and
// whatever is nested under it. A level with no folder of its own is a place the service named a deeper folder at
// without naming this one — a folder at `Archive/2024` where nothing is declared at `Archive`.
interface PathLevel {
    readonly name: string;
    folder: MailFolder | null;
    readonly children: Map<string, PathLevel>;
}

/** The whole tree, as the rows drawing it top to bottom. */
export function folderTreeOf(directory: MailFolderDirectory): readonly FolderTreeRow[] {
    if (directory.accounts.length === 0) {
        return [];
    }

    const accounts = directory.accounts.map(accountRow);

    return directory.accounts.length === 1 ? accounts : [everythingRow(directory), ...accounts];
}

/** Whether a folder may hold another, which is the design's three-level ceiling asked of where the folder sits. */
export function admitsNesting(path: readonly string[]): boolean {
    return path.length < deepestFolder;
}

/**
 * Where the client opens, and where it lands again when what it was scoped to has gone.
 *
 * The inbox, which is where the design opens and where a mail client has opened for thirty years: mail arrives there,
 * and the widest scope is every folder at once — sent, drafts and deleted mail among them, which is a list nobody
 * opens an application to read. Every account's inbox at once where the tree draws that row, and the one account's own
 * where it does not.
 *
 * Answered from the tree's shape rather than from the directory's, which is the whole of why it is answered here: a
 * user with one account is offered no row spanning every account, so a scope the directory still allows can be one
 * nothing draws — and a column with no row drawn as the open one is a column that has stopped saying the one thing it
 * is for.
 */
export function openingScope(directory: MailFolderDirectory): MailScope {
    const [only, ...rest] = directory.accounts;

    if (only === undefined) {
        return everything;
    }

    if (rest.length > 0) {
        return directory.accounts.some((entry) => entry.folders.some((folder) => folder.role === 'Inbox'))
            ? { kind: 'role', role: 'Inbox' }
            : everything;
    }

    const inbox = only.folders.find((folder) => folder.role === 'Inbox');

    return inbox === undefined
        ? { kind: 'account', accountId: only.account.id }
        : { kind: 'folder', accountId: only.account.id, alias: inbox.alias };
}

/**
 * The rows a reader can see, with each row's place among its siblings.
 *
 * Flattened rather than nested, because both things reading this want it flat: the keyboard moves from one visible row
 * to the next one whatever their depth, and a row states its own level rather than being nested inside its parent's
 * list. A tree that draws its rows as a flat list with the level on each is what lets the two agree by construction.
 *
 * `toggled` holds the rows whose fold a reader moved away from what it opens at, rather than the rows that are folded.
 * One set answers both directions because each row states its own default: an account somebody folded away and a
 * subfolder somebody opened are the same act recorded the same way, and a set holding only one of the two would need a
 * second beside it that could disagree with it.
 */
export function visibleRows(rows: readonly FolderTreeRow[], toggled: ReadonlySet<string>): readonly VisibleRow[] {
    const visible: VisibleRow[] = [];

    gather(rows, toggled, visible);

    return visible;
}

/**
 * Whether any row of the tree stands for a key, whichever fold it sits behind.
 *
 * Asked of the whole tree rather than of the rows a reader can see, because a folder somebody chose is still a folder
 * this client is scoped to once its parent is folded away — and a subfolder opens collapsed, so the rows a reader can
 * see leave out exactly the nested ones a scope is most likely to name.
 */
export function namesRow(rows: readonly FolderTreeRow[], key: string): boolean {
    return rows.some((row) => row.key === key || namesRow(row.children, key));
}

/** Whether a row is drawn open, given the folds a reader has moved. */
export function expandedRow(row: FolderTreeRow, toggled: ReadonlySet<string>): boolean {
    return toggled.has(row.key) ? row.opensCollapsed : !row.opensCollapsed;
}

function gather(siblings: readonly FolderTreeRow[], toggled: ReadonlySet<string>, into: VisibleRow[]): void {
    siblings.forEach((row, index) => {
        const opens = row.children.length > 0;
        const expanded = opens && expandedRow(row, toggled);

        into.push({ row, position: index + 1, setSize: siblings.length, expanded: opens ? expanded : null });

        if (expanded) {
            gather(row.children, toggled, into);
        }
    });
}

// Every mailbox at once, and under it the three roles worth reading unified — `rolesAcrossAccounts` names them and
// says why the rest are read in the account they happened in. The counts are summed rather than reported because that
// is what a row stands for: an inbox row spanning three accounts holds what the three inboxes hold. The group's own
// counts are of every folder rather than of those three, because what the row above them stands for is the whole of
// what the person has.
function everythingRow(directory: MailFolderDirectory): FolderTreeRow {
    const roles = rolesAcross(directory);

    return {
        key: scopeKey(everything),
        scope: everything,
        name: '',
        role: null,
        accountId: null,
        alias: null,
        remotePath: null,
        level: 1,
        opensCollapsed: false,
        unreadEmailCount: totalOf(directory, (folder) => folder.unreadEmailCount),
        storedEmailCount: totalOf(directory, (folder) => folder.storedEmailCount),
        children: [...roles.entries()]
            .sort(([one], [other]) => roleRank(one) - roleRank(other))
            .map(([role, folders]) => roleRow(role, folders)),
    };
}

function roleRow(role: MailFolderRole, folders: readonly MailFolder[]): FolderTreeRow {
    const scope: MailScope = { kind: 'role', role };

    return {
        key: scopeKey(scope),
        scope,
        name: '',
        role,
        accountId: null,
        alias: null,
        remotePath: null,
        level: 2,
        opensCollapsed: false,
        unreadEmailCount: sumOf(folders, (folder) => folder.unreadEmailCount),
        storedEmailCount: sumOf(folders, (folder) => folder.storedEmailCount),
        children: [],
    };
}

function accountRow(entry: MailAccountFolders): FolderTreeRow {
    const whole: MailScope = { kind: 'account', accountId: entry.account.id };
    const inbox = entry.folders.find((folder) => folder.role === 'Inbox');

    return {
        key: scopeKey(whole),
        scope: inbox === undefined ? whole : { kind: 'folder', accountId: entry.account.id, alias: inbox.alias },
        name: entry.account.displayName,
        role: null,
        accountId: entry.account.id,
        alias: null,
        remotePath: null,
        level: 1,
        opensCollapsed: false,
        unreadEmailCount: sumOf(entry.folders, (folder) => folder.unreadEmailCount),
        storedEmailCount: sumOf(entry.folders, (folder) => folder.storedEmailCount),
        children: folderRows(entry),
    };
}

function folderRows(entry: MailAccountFolders): readonly FolderTreeRow[] {
    const levels = new Map<string, PathLevel>();

    for (const folder of entry.folders) {
        const [outermost, ...rest] = placeOf(folder);

        if (outermost !== undefined) {
            place(levels, outermost, rest, folder);
        }
    }

    return [...levels.values()].map((level) => rowOfLevel(level, entry.account.id, [], 2)).sort(bySiblingOrder);
}

// Where a folder sits, which is its path. A folder found by the role it plays that no pass has reached yet has no path
// at all, and it sits at the top under the name MailFathom knows it by — the one place it can be drawn without
// guessing where a mail server would put it.
function placeOf(folder: MailFolder): readonly string[] {
    return folder.path.length > 0 ? folder.path : [folder.alias];
}

// Walks a folder's path down the levels built so far, adding what is missing, and binds the folder to the last of
// them. Recursive rather than iterative so nothing has to assert that the level it ended on exists.
function place(levels: Map<string, PathLevel>, name: string, rest: readonly string[], folder: MailFolder): void {
    const level = levels.get(name) ?? { name, folder: null, children: new Map<string, PathLevel>() };

    levels.set(name, level);

    const [next, ...deeper] = rest;

    if (next === undefined) {
        level.folder = folder;
    } else {
        place(level.children, next, deeper, folder);
    }
}

function rowOfLevel(level: PathLevel, accountId: string, above: readonly string[], depth: number): FolderTreeRow {
    const segments = [...above, level.name];
    const children = [...level.children.values()]
        .map((nested) => rowOfLevel(nested, accountId, segments, depth + 1))
        .sort(bySiblingOrder);

    if (level.folder === null) {
        return {
            // Keyed by where it sits in which mailbox, because two mailboxes nest folders under levels of the same name
            // and a key that collided would fold both of them away together. The levels are joined as a list rather
            // than by a separator, since a mail server's own delimiter may be any character a level name could hold.
            key: `level:${accountId}:${JSON.stringify(segments)}`,
            scope: null,
            // What the mail server calls this level, which is the one name there is for a place nothing is declared at.
            name: level.name,
            role: null,
            accountId,
            alias: null,
            remotePath: null,
            level: depth,
            opensCollapsed: true,
            unreadEmailCount: null,
            storedEmailCount: null,
            children,
        };
    }

    return folderRow(level.folder, accountId, depth, children);
}

function folderRow(
    folder: MailFolder,
    accountId: string,
    depth: number,
    children: readonly FolderTreeRow[],
): FolderTreeRow {
    const scope: MailScope = { kind: 'folder', accountId, alias: folder.alias };

    return {
        key: scopeKey(scope),
        scope,
        name: folderName(folder),
        role: folder.role,
        accountId,
        alias: folder.alias,
        remotePath: folder.path,
        level: depth,
        opensCollapsed: true,
        unreadEmailCount: folder.unreadEmailCount,
        storedEmailCount: folder.storedEmailCount,
        children,
    };
}

// What a person recognizes the folder by, which is the last level of where it sits on their mail server: the alias is
// canonically upper-cased so that one folder is one value in a database whose collation MailFathom does not control,
// and reading a name off it would draw a mailbox in capitals nobody typed. A folder with no path has no such level to
// read, and its alias is what is left.
function folderName(folder: MailFolder): string {
    return folder.path.at(-1) ?? folder.alias;
}

// A folder playing a role comes before one that plays none, in the order roles are offered in; the rest read as a
// mailbox reads, by the name on the row. Sorting by name is the client's decision rather than the service's, which
// orders by an alias no screen shows.
function bySiblingOrder(one: FolderTreeRow, other: FolderTreeRow): number {
    const byRole = roleRank(one.role) - roleRank(other.role);

    return byRole === 0 ? one.name.localeCompare(other.name) : byRole;
}

// Which folders play each of the roles offered across every account, gathered from every account at once.
//
// Only the roles `rolesAcrossAccounts` names are gathered at all, which is where the unified group's *three* rows come
// from rather than one per role any account happens to play: what a role is worth reading unified is decided there,
// beside the order the roles are offered in, because the dialog that files mail into a folder and the scope a returning
// reader is put back into both answer to the same decision.
function rolesAcross(directory: MailFolderDirectory): ReadonlyMap<MailFolderRole, readonly MailFolder[]> {
    const roles = new Map<MailFolderRole, MailFolder[]>();

    for (const entry of directory.accounts) {
        for (const folder of entry.folders) {
            if (folder.role === null || !rolesAcrossAccounts.includes(folder.role)) {
                continue;
            }

            const carrying = roles.get(folder.role) ?? [];

            carrying.push(folder);
            roles.set(folder.role, carrying);
        }
    }

    return roles;
}

function sumOf(folders: readonly MailFolder[], count: (folder: MailFolder) => number): number {
    return folders.reduce((total, folder) => total + count(folder), 0);
}

function totalOf(directory: MailFolderDirectory, count: (folder: MailFolder) => number): number {
    return directory.accounts.reduce((total, entry) => total + sumOf(entry.folders, count), 0);
}
