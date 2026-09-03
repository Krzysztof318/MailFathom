// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import {
    isSynchronizationState,
    maximumAccountsInDirectory,
    parseMailAccount,
    type MailAccount,
    type MailSynchronizationState,
} from './mailAccounts';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// The owner's mailboxes and the folders in them, which the service answers in one exchange because they are one tree
// on screen. The account inside each entry is the accounts route's own shape, parsed by the accounts route's own
// parser, so the two reads cannot come to disagree about what a mailbox is.

/** The route the owner's folders are served at, relative to the client prefix. */
export const mailFoldersRoute = '/folders';

/**
 * The role a folder plays for its account, independently of what its server calls it.
 *
 * It is what a client cannot work out for itself: special-use folders are advertised by server attribute rather than
 * by name, and the names differ per provider and per language. A folder configuration labelled with none carries
 * `null` here, and nothing in this package guesses one from a name.
 */
export type MailFolderRole =
    'Inbox' | 'Archive' | 'Drafts' | 'Sent' | 'Junk' | 'Trash' | 'All' | 'Flagged' | 'Important' | 'Outbox';

/** One folder of one account, as a screen drawing a tree reads it. */
export interface MailFolder {
    /** MailFathom's own name for the folder, which is what every other route on this surface names it by. */
    readonly alias: string;

    /** The role the folder plays, or `null` where configuration labels it with none. */
    readonly role: MailFolderRole | null;

    /** The folder's place on its mail server, outermost level first, and empty where nothing has bound the alias yet. */
    readonly path: readonly string[];

    /** How many of the folder's emails this deployment holds, which is not how many the mailbox has. */
    readonly storedEmailCount: number;

    /** How many of those the mail server last reported without the seen flag. */
    readonly unreadEmailCount: number;

    readonly synchronizationState: MailSynchronizationState;
    readonly lastSynchronizedAt: string | null;
    readonly behind: boolean;
}

/** One of the owner's accounts and the folders beneath it. */
export interface MailAccountFolders {
    readonly account: MailAccount;
    readonly folders: readonly MailFolder[];
}

/** The owner's whole tree, beside the deployment-wide switch saying whether any of it is being refreshed at all. */
export interface MailFolderDirectory {
    readonly synchronizationEnabled: boolean;
    readonly accounts: readonly MailAccountFolders[];
}

// What one account may answer with before the tree is refused unread. A mailbox holds tens of folders in practice and
// the service bounds them by what configuration admits, so this is far above anything real and exists for the answer
// that is not — checked during the walk rather than after it, because a bound applied afterwards is not a bound.
const maximumFoldersInAccount = 1_024;

// How deep a folder's place on its server may be. Servers nest a handful of levels; a path longer than this is an
// answer no mailbox produced, and drawing it would indent a tree past anything a screen can show.
const maximumHierarchyLevels = 32;

const roles: readonly MailFolderRole[] = [
    'Inbox',
    'Archive',
    'Drafts',
    'Sent',
    'Junk',
    'Trash',
    'All',
    'Flagged',
    'Important',
    'Outbox',
];

/** Reads the signed-in owner's mailboxes and folders, answering an expected failure as a value rather than by throwing. */
export function readMailFolders(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<MailFolderDirectory>> {
    return spanned(`GET ${mailFoldersRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailFoldersRoute),
            headers: headersFor(session),
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const directory = parseDirectory(response.body);

        return directory === null ? failed('unreadable', response.status) : read(directory);
    });
}

function parseDirectory(body: string): MailFolderDirectory | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null) {
        return null;
    }

    const synchronizationEnabled = record['synchronizationEnabled'];
    const entries = record['accounts'];
    if (typeof synchronizationEnabled !== 'boolean' || !Array.isArray(entries)) {
        return null;
    }

    if (entries.length > maximumAccountsInDirectory) {
        return null;
    }

    const accounts: MailAccountFolders[] = [];
    for (const entry of entries) {
        const account = parseAccountFolders(entry);
        if (account === null) {
            return null;
        }

        accounts.push(account);
    }

    return { synchronizationEnabled, accounts };
}

function parseAccountFolders(value: unknown): MailAccountFolders | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const account = parseMailAccount(record['account']);
    const entries = record['folders'];
    if (account === null || !Array.isArray(entries) || entries.length > maximumFoldersInAccount) {
        return null;
    }

    const folders: MailFolder[] = [];
    for (const entry of entries) {
        const folder = parseFolder(entry);
        if (folder === null) {
            return null;
        }

        folders.push(folder);
    }

    return { account, folders };
}

function parseFolder(value: unknown): MailFolder | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const alias = record['alias'];
    const role = record['role'] ?? null;
    const storedEmailCount = record['storedEmailCount'];
    const unreadEmailCount = record['unreadEmailCount'];
    const synchronizationState = record['synchronizationState'];
    const lastSynchronizedAt = record['lastSynchronizedAt'] ?? null;
    const behind = record['behind'];

    if (typeof alias !== 'string' || typeof behind !== 'boolean') {
        return null;
    }

    if (role !== null && !isFolderRole(role)) {
        return null;
    }

    const path = parsePath(record['path']);
    if (path === null) {
        return null;
    }

    if (!isCount(storedEmailCount) || !isCount(unreadEmailCount)) {
        return null;
    }

    if (!isSynchronizationState(synchronizationState)) {
        return null;
    }

    if (lastSynchronizedAt !== null && typeof lastSynchronizedAt !== 'string') {
        return null;
    }

    return {
        alias,
        role,
        path,
        storedEmailCount,
        unreadEmailCount,
        synchronizationState,
        lastSynchronizedAt,
        behind,
    };
}

function parsePath(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > maximumHierarchyLevels) {
        return null;
    }

    const levels: string[] = [];
    for (const level of value) {
        if (typeof level !== 'string') {
            return null;
        }

        levels.push(level);
    }

    return levels;
}

function isFolderRole(value: unknown): value is MailFolderRole {
    return typeof value === 'string' && roles.includes(value as MailFolderRole);
}

// A count is a whole number of messages this deployment holds, so a fraction, a negative, and a value past what
// arithmetic here stays exact for are each an answer no mailbox produced.
function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
