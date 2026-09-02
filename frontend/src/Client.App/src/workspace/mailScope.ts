// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFolderRole } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';

// What the client is looking at, which the list, the search, and the next question are all asked against. It is one
// value rather than an account beside a folder, because the four things somebody can point at are not a pair: an
// account with no folder and a folder with no account are both meaningless, and the role scopes — one mailbox's worth
// of inbox is one thing, every mailbox's inbox at once is another — have no account to name at all.
//
// The last of those is what several mailboxes in one workspace actually means. Somebody with a work account and two
// personal ones wants the inbox of all three as often as they want one, and wants sent to mean sent from any of them.

/** What every read and every question is scoped to. */
export type MailScope =
    /** Every folder of every account the owner has. */
    | { readonly kind: 'everything' }

    /** The folders playing one role, across every account that has one — every inbox at once, every sent folder at once. */
    | { readonly kind: 'role'; readonly role: MailFolderRole }

    /** Every folder of one account. */
    | { readonly kind: 'account'; readonly accountId: string }

    /** One folder of one account, named by the alias everything on the client surface names a folder by. */
    | { readonly kind: 'folder'; readonly accountId: string; readonly alias: string };

/** Where the client opens: everything the owner has, which is the widest scope rather than an unset one. */
export const everything: MailScope = { kind: 'everything' };

// The order the roles are offered in, which is the order a mail client has shown them in for thirty years rather than
// the order the service happens to declare them. Exhaustive by its own type, so a role added to the client surface
// fails to compile here until somebody has decided where it belongs.
const roleOrder: Readonly<Record<MailFolderRole, number>> = {
    Inbox: 0,
    Drafts: 1,
    Sent: 2,
    Archive: 3,
    Junk: 4,
    Trash: 5,
    Flagged: 6,
    Important: 7,
    All: 8,
    Outbox: 9,
};

/**
 * What each role is called on the screen, because a role is named by what it is rather than by whatever a provider
 * called the folder playing it, in whatever language.
 *
 * Exhaustive by its own type, so a role added to the client surface fails to compile until it has a name — and stated
 * here rather than in the tree that drew one first, because the search offers the same roles as folders to search in.
 */
export const folderRoleLabels: Readonly<Record<MailFolderRole, MessageKey>> = {
    Inbox: 'folder.inbox',
    Drafts: 'folder.drafts',
    Sent: 'folder.sent',
    Archive: 'folder.archive',
    Junk: 'folder.junk',
    Trash: 'folder.trash',
    Flagged: 'folder.flagged',
    Important: 'folder.important',
    All: 'folder.all',
    Outbox: 'folder.outbox',
};

/** Whether the value is one of the roles this surface publishes. */
export function isMailFolderRole(value: unknown): value is MailFolderRole {
    return typeof value === 'string' && Object.hasOwn(roleOrder, value);
}

/** The roles this surface publishes, in the order they are offered in. */
export const folderRoles: readonly MailFolderRole[] = Object.keys(roleOrder).filter(isMailFolderRole);

/** Where a role falls in the order they are offered in; a folder carrying none falls after every one that does. */
export function roleRank(role: MailFolderRole | null): number {
    return role === null ? Object.keys(roleOrder).length : roleOrder[role];
}

/**
 * The scope's identity as one string.
 *
 * It is what a row of the tree is keyed and compared by, so nothing has to write a comparison per scope shape and a
 * fifth shape cannot be forgotten by one of them.
 */
export function scopeKey(scope: MailScope): string {
    switch (scope.kind) {
        case 'everything':
            return 'everything';
        case 'role':
            return `role:${scope.role}`;
        case 'account':
            return `account:${scope.accountId}`;
        case 'folder':
            return `folder:${scope.accountId}:${scope.alias}`;
    }
}

/** Whether two scopes point at the same thing. */
export function sameScope(one: MailScope, other: MailScope): boolean {
    return scopeKey(one) === scopeKey(other);
}

/** The account a scope names, or `null` where it spans every account the owner has. */
export function accountInScope(scope: MailScope): string | null {
    return scope.kind === 'account' || scope.kind === 'folder' ? scope.accountId : null;
}

/**
 * The account and the folder a scope names on the client surface, where it names either.
 *
 * Every read scoped to a mailbox asks with these two, so they are answered here rather than once per screen: the list
 * and the search would otherwise each hold their own reading of what a role scope is, and a fifth scope shape would
 * have to be found in both.
 */
export function namedInScope(scope: MailScope): { readonly account: string | null; readonly folder: string | null } {
    switch (scope.kind) {
        case 'everything':
            return { account: null, folder: null };
        case 'role':
            return { account: null, folder: `role:${scope.role}` };
        case 'account':
            return { account: scope.accountId, folder: null };
        case 'folder':
            return { account: scope.accountId, folder: scope.alias };
    }
}

/**
 * Whether the scope is junk somebody pointed at, which is the one case a read asks for junk without being told to.
 *
 * The deployment withholds junk from a read spanning folders, so a reader who has opened their junk folder — or the
 * role that is every account's junk folder at once — would be shown an empty one. Both of those have already excluded
 * everything but junk, so asking cannot reach anything the reader did not point at. Every other role spans many
 * folders across many accounts and is exactly the list junk is withheld from, so it is left out there.
 */
export function scopePointsAtJunk(scope: MailScope): boolean {
    return scope.kind === 'folder' || (scope.kind === 'role' && scope.role === 'Junk');
}

/** The scope naming one whole account, or everything where no account was named. */
export function scopeOfAccount(accountId: string | null): MailScope {
    return accountId === null ? everything : { kind: 'account', accountId };
}
