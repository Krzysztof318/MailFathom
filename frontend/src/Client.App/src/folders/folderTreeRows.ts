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
// each account carries its own folders, nested by the alias that names them. And a folder that plays a role is placed
// by that role rather than by its name, because a name is whatever a provider chose in whatever language.
//
// **The nesting is the alias's and not the remote path's.** An alias is MailFathom's own name for a folder and is what
// every other route on this surface names it by, so a tree built from it is a tree whose rows can be acted on — a
// folder created inside another is one whose alias extends its parent's, and nothing has to work out which server path
// that turned into. The remote path is where the folder sits on somebody's mail server, which is a second hierarchy
// that a provider, a delimiter, and a language each get a say in; it is what the folder dialog edits and what the last
// level of a row's name is read from, and it decides nothing about the shape of the tree.

/** How many segments an alias may carry, which is how deep a mailbox nests: `Inbox/Projects/2026` and no further. */
export const deepestAlias = 3;

/** The one character an alias nests on, which every reading of one here splits and joins by. */
export const aliasSeparator = '/';

/** One row of the tree, whatever it stands for: the whole workspace, a role across it, an account, or a folder. */
export interface FolderTreeRow {
    /** What identifies the row — what is folded, what is focused, and what is compared against the current scope. */
    readonly key: string;

    /**
     * What selecting the row scopes the client to, or `null` for a level of an alias the service named no folder for.
     *
     * Not the same thing as the key, and an account's row is where the two part: pressing a mailbox's name means its
     * inbox rather than every folder it has at once, so the row is keyed by the account and scopes to the inbox. What
     * that leaves is a heading that selects without ever drawing as the selected row, which is how the design draws a
     * group: the row that lights up is the inbox beneath it, and that is the row somebody was pointing at.
     */
    readonly scope: MailScope | null;

    /** The name whatever this row stands for has: a mailbox's display name, a level of an alias, or a folder's own. */
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

// A level of an alias while the tree is being built: the folder bound to exactly that alias where there is one, and
// whatever is nested under it. A level with no folder of its own is an alias the service named a deeper folder for
// without naming this one — a mapping bound to `Archive/2024` where nothing is bound to `Archive`.
interface AliasLevel {
    readonly name: string;
    folder: MailFolder | null;
    readonly children: Map<string, AliasLevel>;
}

/** The whole tree, as the rows drawing it top to bottom. */
export function folderTreeOf(directory: MailFolderDirectory): readonly FolderTreeRow[] {
    if (directory.accounts.length === 0) {
        return [];
    }

    const accounts = directory.accounts.map(accountRow);

    return directory.accounts.length === 1 ? accounts : [everythingRow(directory), ...accounts];
}

/** The segments an alias nests by, which is what decides both a row's parent and whether another may sit under it. */
export function aliasSegments(alias: string): readonly string[] {
    return alias.split(aliasSeparator).filter((segment) => segment.length > 0);
}

/** Whether a folder may hold another, which is the design's three-segment ceiling asked of one alias. */
export function admitsNesting(alias: string): boolean {
    return aliasSegments(alias).length < deepestAlias;
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
    const levels = new Map<string, AliasLevel>();

    for (const folder of entry.folders) {
        const [outermost, ...rest] = aliasSegments(folder.alias);

        // An alias of nothing but separators is an answer no mapping produced, and it has nowhere in the tree to go.
        if (outermost !== undefined) {
            place(levels, outermost, rest, folder);
        }
    }

    return [...levels.values()].map((level) => rowOfLevel(level, entry.account.id, [], 2)).sort(bySiblingOrder);
}

// Walks a folder's alias down the levels built so far, adding what is missing, and binds the folder to the last of
// them. Recursive rather than iterative so nothing has to assert that the level it ended on exists.
function place(levels: Map<string, AliasLevel>, name: string, rest: readonly string[], folder: MailFolder): void {
    const level = levels.get(name) ?? { name, folder: null, children: new Map<string, AliasLevel>() };

    levels.set(name, level);

    const [next, ...deeper] = rest;

    if (next === undefined) {
        level.folder = folder;
    } else {
        place(level.children, next, deeper, folder);
    }
}

function rowOfLevel(level: AliasLevel, accountId: string, above: readonly string[], depth: number): FolderTreeRow {
    const segments = [...above, level.name];
    const alias = segments.join(aliasSeparator);
    const children = [...level.children.values()]
        .map((nested) => rowOfLevel(nested, accountId, segments, depth + 1))
        .sort(bySiblingOrder);

    if (level.folder === null) {
        return {
            // Keyed by where it sits rather than by what it is called, because two mailboxes nest folders of the same
            // name and a key that collided would fold both of them away together.
            key: `level:${accountId}:${alias}`,
            scope: null,
            name: levelName(segments, children),
            role: null,
            accountId,
            alias,
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

// What a person recognizes a level nothing is bound to by. Its own alias segment is canonically upper-cased, exactly
// as a bound folder's alias is, so drawing it would put a mailbox in capitals nobody typed — the defect `folderName`
// below avoids, cured the same way: the remote path of a folder nested under this level records what somebody wrote.
//
// The two are lined up from their ends rather than by position, because an alias and a remote path nest the same
// folder to whatever depths MailFathom and the mail server each chose: an alias two levels deep may sit three
// directories down. A path too short to reach this level, and a level nothing is bound anywhere beneath, both leave
// the alias segment as the only name there is.
function levelName(segments: readonly string[], children: readonly FolderTreeRow[]): string {
    const own = segments.at(-1) ?? '';
    const bound = nearestBound(children);

    return bound === null ? own : (bound.path.at(-1 - (aliasSegments(bound.alias).length - segments.length)) ?? own);
}

// The first folder the service named anywhere below these rows, read depth first so the nearest one answers, and as
// the two values a level's name is read from rather than as the row itself — which is what says both are there.
function nearestBound(
    rows: readonly FolderTreeRow[],
): { readonly alias: string; readonly path: readonly string[] } | null {
    for (const row of rows) {
        if (row.alias !== null && row.remotePath !== null) {
            return { alias: row.alias, path: row.remotePath };
        }

        const deeper = nearestBound(row.children);

        if (deeper !== null) {
            return deeper;
        }
    }

    return null;
}

// What a person recognizes the folder by, which is the last level of where it sits on their mail server: the alias is
// canonically upper-cased so that one folder is one value in a database whose collation MailFathom does not control,
// and reading a name off it would draw a mailbox in capitals nobody typed. An alias nothing has bound yet has no such
// level to read, and its own last segment is what is left.
function folderName(folder: MailFolder): string {
    const deepest = folder.path.at(-1) ?? aliasSegments(folder.alias).at(-1);

    return deepest ?? folder.alias;
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
