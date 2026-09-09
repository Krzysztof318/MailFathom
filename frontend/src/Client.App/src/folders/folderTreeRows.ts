// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAccountFolders, MailFolder, MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import { everything, roleRank, scopeKey, type MailScope } from '../workspace/mailScope';

// What the service answered, turned into the rows a tree draws. It is a function over values rather than anything a
// component does while rendering: the shape of the tree is the interesting decision here, and a decision that can be
// read as a value can be tested as one.
//
// Four things it decides. The user's mailboxes are one workspace rather than four applications, so the tree opens with
// every account at once and the roles that span them — the inbox of all three accounts is a thing somebody wants as
// often as the inbox of one. That group is what several mailboxes are for, so a user holding exactly one account is
// not offered it: it would draw that account's folders a second time under a heading meaning the same thing. Below it
// each account carries its own folders, nested the way its mail server nests them, which is what the levels of a
// folder's path are for. And a folder that plays a role is placed by that role rather than by its name, because a name
// is whatever a provider chose in whatever language.

/** One row of the tree, whatever it stands for: the whole workspace, a role across it, an account, or a folder. */
export interface FolderTreeRow {
    /** What identifies the row — what is folded, what is focused, and what is compared against the current scope. */
    readonly key: string;

    /**
     * What selecting the row scopes the client to, or `null` for a level of a path the service named no folder for.
     *
     * Not the same thing as the key, and an account's row is where the two part: pressing a mailbox's name means its
     * inbox rather than every folder it has at once, so the row is keyed by the account and scopes to the inbox. What
     * that leaves is a heading that selects without ever drawing as the selected row, which is how the design draws a
     * group: the row that lights up is the inbox beneath it, and that is the row somebody was pointing at.
     */
    readonly scope: MailScope | null;

    /** The name whatever this row stands for has: a mailbox's display name, a level of a path, or a folder's alias. */
    readonly name: string;

    /** The role this row stands for, where it has one — in which case the role names it rather than `name` does. */
    readonly role: MailFolderRole | null;

    /** How deep the row sits, counted from one, which is what a tree reports as its level. */
    readonly level: number;

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

// A level of a folder's path while the tree is being built: the folder bound to exactly that path where there is one,
// and whatever is nested under it. A level with no folder of its own is a path the service named a deeper folder for
// without naming this one — a mapping bound to `Archive/2024` where nothing is bound to `Archive`.
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
 */
export function visibleRows(rows: readonly FolderTreeRow[], collapsed: ReadonlySet<string>): readonly VisibleRow[] {
    const visible: VisibleRow[] = [];

    gather(rows, collapsed, visible);

    return visible;
}

function gather(siblings: readonly FolderTreeRow[], collapsed: ReadonlySet<string>, into: VisibleRow[]): void {
    siblings.forEach((row, index) => {
        const opens = row.children.length > 0;
        const expanded = opens && !collapsed.has(row.key);

        into.push({ row, position: index + 1, setSize: siblings.length, expanded: opens ? expanded : null });

        if (expanded) {
            gather(row.children, collapsed, into);
        }
    });
}

// Every mailbox at once, and under it the roles at least one of them plays. The counts are summed rather than reported
// because that is what the row stands for: an inbox row spanning three accounts holds what the three inboxes hold.
function everythingRow(directory: MailFolderDirectory): FolderTreeRow {
    const roles = rolesAcross(directory);

    return {
        key: scopeKey(everything),
        scope: everything,
        name: '',
        role: null,
        level: 1,
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
        level: 2,
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
        level: 1,
        unreadEmailCount: sumOf(entry.folders, (folder) => folder.unreadEmailCount),
        storedEmailCount: sumOf(entry.folders, (folder) => folder.storedEmailCount),
        children: folderRows(entry),
    };
}

function folderRows(entry: MailAccountFolders): readonly FolderTreeRow[] {
    const levels = new Map<string, PathLevel>();
    const unbound: MailFolder[] = [];

    for (const folder of entry.folders) {
        const [outermost, ...rest] = folder.path;

        if (outermost === undefined) {
            unbound.push(folder);
        } else {
            place(levels, outermost, rest, folder);
        }
    }

    const rows = [
        ...[...levels.values()].map((level) => rowOfLevel(level, entry.account.id, [], 2)),
        ...unbound.map((folder) => folderRow(folder, folder.alias, entry.account.id, 2, [])),
    ];

    return rows.sort(bySiblingOrder);
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
    const path = [...above, level.name];
    const children = [...level.children.values()]
        .map((nested) => rowOfLevel(nested, accountId, path, depth + 1))
        .sort(bySiblingOrder);

    if (level.folder === null) {
        return {
            // Keyed by where it sits rather than by what it is called, because two mailboxes nest folders of the same
            // name and a key that collided would fold both of them away together.
            key: `level:${accountId}:${path.join('/')}`,
            scope: null,
            name: level.name,
            role: null,
            level: depth,
            unreadEmailCount: null,
            storedEmailCount: null,
            children,
        };
    }

    return folderRow(level.folder, level.name, accountId, depth, children);
}

function folderRow(
    folder: MailFolder,
    name: string,
    accountId: string,
    depth: number,
    children: readonly FolderTreeRow[],
): FolderTreeRow {
    const scope: MailScope = { kind: 'folder', accountId, alias: folder.alias };

    return {
        key: scopeKey(scope),
        scope,
        name,
        role: folder.role,
        level: depth,
        unreadEmailCount: folder.unreadEmailCount,
        storedEmailCount: folder.storedEmailCount,
        children,
    };
}

// A folder playing a role comes before one that plays none, in the order roles are offered in; the rest read as a
// mailbox reads, by the name on the row. Sorting by name is the client's decision rather than the service's, which
// orders by an alias no screen shows.
function bySiblingOrder(one: FolderTreeRow, other: FolderTreeRow): number {
    const byRole = roleRank(one.role) - roleRank(other.role);

    return byRole === 0 ? one.name.localeCompare(other.name) : byRole;
}

function rolesAcross(directory: MailFolderDirectory): ReadonlyMap<MailFolderRole, readonly MailFolder[]> {
    const roles = new Map<MailFolderRole, MailFolder[]>();

    for (const entry of directory.accounts) {
        for (const folder of entry.folders) {
            if (folder.role === null) {
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
