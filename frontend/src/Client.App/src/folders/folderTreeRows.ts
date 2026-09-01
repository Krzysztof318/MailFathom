// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    MailAccountFolders,
    MailFolder,
    MailFolderDirectory,
    MailFolderRole,
    MailSynchronizationState,
} from '@mailfathom/client-backend';
import { everything, roleRank, scopeKey, type MailScope } from '../workspace/mailScope';

// What the service answered, turned into the rows a tree draws. It is a function over values rather than anything a
// component does while rendering: the shape of the tree is the interesting decision here, and a decision that can be
// read as a value can be tested as one.
//
// Three things it decides. The owner's mailboxes are one workspace rather than four applications, so the tree opens
// with every account at once and the roles that span them — the inbox of all three accounts is a thing somebody wants
// as often as the inbox of one. Below that each account carries its own folders, nested the way its mail server nests
// them, which is what the levels of a folder's path are for. And a folder that plays a role is placed by that role
// rather than by its name, because a name is whatever a provider chose in whatever language.

/** One row of the tree, whatever it stands for: the whole workspace, a role across it, an account, or a folder. */
export interface FolderTreeRow {
    /** What identifies the row — what is folded, what is focused, and what is compared against the current scope. */
    readonly key: string;

    /** What selecting the row scopes the client to, or `null` for a level of a path the service named no folder for. */
    readonly scope: MailScope | null;

    /** The name whatever this row stands for has: a mailbox's display name, a level of a path, or a folder's alias. */
    readonly name: string;

    /** The role this row stands for, where it has one — in which case the role names it rather than `name` does. */
    readonly role: MailFolderRole | null;

    /** How deep the row sits, counted from one, which is what a tree reports as its level. */
    readonly level: number;

    /** How current the local copy is, or `null` for a row that stands for more than one thing. */
    readonly state: MailSynchronizationState | null;

    /** Whether the last attempt ended with mail it had not yet taken in. */
    readonly behind: boolean;

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

    return [everythingRow(directory), ...directory.accounts.map(accountRow)];
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
        state: null,
        behind: false,
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
        state: null,
        behind: false,
        unreadEmailCount: sumOf(folders, (folder) => folder.unreadEmailCount),
        storedEmailCount: sumOf(folders, (folder) => folder.storedEmailCount),
        children: [],
    };
}

function accountRow(entry: MailAccountFolders): FolderTreeRow {
    const scope: MailScope = { kind: 'account', accountId: entry.account.id };

    return {
        key: scopeKey(scope),
        scope,
        name: entry.account.displayName,
        role: null,
        level: 1,
        state: entry.account.synchronizationState,
        behind: entry.account.behind,
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
            state: null,
            behind: false,
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
        state: folder.synchronizationState,
        behind: folder.behind,
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
