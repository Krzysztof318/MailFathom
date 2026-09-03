// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFolderDirectory } from '@mailfathom/client-backend';
import type { MarkedIn } from '../readMarking/useReadMarking';

// A folder's unread count and the rows of that folder have to say the same thing, and for minutes at a time the
// deployment's own answer does not: marking a message read is a durable mutation the account's own pass carries to the
// mail server, and the stored count follows the observation rather than the mutation. So the count a reader sees is
// what the deployment reported, less what this client has marked read in that folder since.
//
// It is applied to the directory before the tree is built rather than to a row afterwards, which is what keeps the four
// levels of the tree agreeing with each other: the account row, the role rows across accounts, and the whole-workspace
// row are each a sum over the same folders, so correcting the folders corrects all of them and nothing sums a corrected
// number twice.

/**
 * The directory with each folder's unread count reduced by what this client has marked read in it.
 *
 * @param directory What the folders route answered.
 * @param marked What this client has marked read, by the message, with the folder each was counted in.
 * @returns The directory to draw, or the one it was given where nothing has been marked.
 */
export function unreadAfterMarking(
    directory: MailFolderDirectory,
    marked: ReadonlyMap<string, MarkedIn>,
): MailFolderDirectory {
    if (marked.size === 0) {
        return directory;
    }

    const markedPerFolder = new Map<string, number>();

    for (const { account, folder } of marked.values()) {
        const key = folderKey(account, folder);

        markedPerFolder.set(key, (markedPerFolder.get(key) ?? 0) + 1);
    }

    return {
        ...directory,
        accounts: directory.accounts.map((entry) => ({
            ...entry,
            folders: entry.folders.map((folder) => ({
                ...folder,
                // Floored at nothing rather than trusted to stay positive: a folder synchronized between the marking
                // and this read already reports the message as read, and subtracting again would count it twice.
                unreadEmailCount: Math.max(
                    folder.unreadEmailCount - (markedPerFolder.get(folderKey(entry.account.id, folder.alias)) ?? 0),
                    0,
                ),
            })),
        })),
    };
}

// A message is placed by its account and its folder together, because two mailboxes name a folder the same way and a
// key that collided would take one account's reading off the other's count. The separator is written as an escape
// rather than as the byte, which the repository refuses in a tracked file: a literal one makes the source binary to
// `grep` and to `git diff`. It is that byte rather than a space or a slash because a mail server's folder alias may
// carry either, and a separator a name can contain is a separator two different pairs can spell the same way.
function folderKey(account: string, folder: string): string {
    return `${account}\u0000${folder}`;
}
