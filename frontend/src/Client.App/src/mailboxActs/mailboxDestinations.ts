// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFolder, MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import type { ActedMessage, MailboxAct } from './useMailboxActs';

// Where an act files a message, worked out from the folders each account has rather than guessed from a name.
//
// Archiving and deleting are folder moves rather than flags, and neither exists as an idea below this line: what
// *archive* means for an account is the folder its configuration labels `Archive`, and what *delete* means is the one
// labelled `Trash`. An account labelled with neither has nowhere for the act to go, which is a sentence the control
// says before it is pressed rather than a refusal that arrives after it.
//
// Filing somewhere chosen is bounded by what a mail server can do: a message moves between folders of its own account
// and nowhere else. So a selection spanning two accounts has no one destination to offer, and that is refused here
// rather than met as half a batch landing.

/** One folder a move may name, as the dialog offering it draws it. */
export interface MoveDestination {
    /** MailFathom's own name for the folder, which is what the move route names it by. */
    readonly alias: string;

    /** What the folder is called, which is its place on the server rather than its alias. */
    readonly name: string;
}

/** Why an act cannot be performed on the messages it was asked about, each a sentence of its own on the screen. */
export type ActRefusal =
    /** The credential this client signed in under may not write what the act writes. */
    | 'notOffered'

    /** Nothing is picked out or open, so the act has nothing to be about. */
    | 'nothingToActOn'

    /** An account among them labels no folder as its archive. */
    | 'noArchiveFolder'

    /** An account among them labels no folder as its trash. */
    | 'noTrashFolder'

    /** The messages are in more than one account, and one folder belongs to one account. */
    | 'severalAccounts'

    /** The one account has no other folder to file into. */
    | 'noOtherFolder'

    /** This client has not read the folders, so it does not know where the act would file to. */
    | 'foldersUnknown';

/** The folder an account labels with that role, or `null` where its configuration labels none. */
export function folderWithRole(
    directory: MailFolderDirectory | null,
    account: string,
    role: MailFolderRole,
): string | null {
    return foldersOf(directory, account).find((folder) => folder.role === role)?.alias ?? null;
}

/** The accounts the named messages are in, each named once. */
export function accountsAmong(messages: readonly ActedMessage[]): readonly string[] {
    return [...new Set(messages.map((message) => message.account))];
}

/**
 * The folders the named messages could be filed into, which are the folders of the one account they are all in.
 *
 * The folder they are already in is offered like any other: the deployment answers a message already there as its own
 * outcome, so leaving it out would be this client deciding what a folder holds from a list it read minutes ago.
 */
export function destinationsFor(
    directory: MailFolderDirectory | null,
    messages: readonly ActedMessage[],
): readonly MoveDestination[] {
    const accounts = accountsAmong(messages);

    if (accounts.length !== 1) {
        return [];
    }

    return foldersOf(directory, accounts[0] ?? '')
        .map((folder) => ({ alias: folder.alias, name: nameOf(folder) }))
        .sort((one, other) => one.name.localeCompare(other.name));
}

/**
 * Why the act cannot be performed on those messages, or `null` where it can.
 *
 * @param act What is being asked for.
 * @param messages The messages it would be about.
 * @param directory The owner's folders, or `null` where they have not been read.
 * @param offered Whether the credential may write the flags an act needs and move mail.
 */
export function refusalFor(
    act: MailboxAct,
    messages: readonly ActedMessage[],
    directory: MailFolderDirectory | null,
    offered: { readonly flags: boolean; readonly moves: boolean },
): ActRefusal | null {
    if (messages.length === 0) {
        return 'nothingToActOn';
    }

    if (!(act === 'flag' || act === 'markUnread' ? offered.flags : offered.moves)) {
        return 'notOffered';
    }

    if (act === 'flag' || act === 'markUnread') {
        return null;
    }

    // The three acts below are folder moves, and folders this client has not read are not folders an account does not
    // have. Said apart for that reason: a read that failed and a mailbox labelling no archive would otherwise reach a
    // reader as the same sentence, and only one of the two is something they can do anything about.
    if (directory === null) {
        return 'foldersUnknown';
    }

    switch (act) {
        case 'archive':
            return everyAccountHas(directory, messages, 'Archive') ? null : 'noArchiveFolder';
        case 'delete':
            return everyAccountHas(directory, messages, 'Trash') ? null : 'noTrashFolder';
        case 'move':
            return accountsAmong(messages).length > 1
                ? 'severalAccounts'
                : movesSomething(directory, messages)
                  ? null
                  : 'noOtherFolder';
    }
}

// Whether any folder on offer would actually take a message somewhere it is not. An account with one folder offers
// exactly the folder its mail already sits in, which is a dialog with nothing in it to choose — while a selection
// spread across two folders of a two-folder account has two destinations that each move half of it.
function movesSomething(directory: MailFolderDirectory, messages: readonly ActedMessage[]): boolean {
    return destinationsFor(directory, messages).some((destination) =>
        messages.some((message) => message.folder !== destination.alias),
    );
}

/**
 * Where each message goes for an act that files it, one destination per message.
 *
 * Empty for an act that files nothing, and for one whose destination an account does not have — which `refusalFor`
 * has already said before a control offering it could be pressed.
 */
export function filingFor(
    act: MailboxAct,
    messages: readonly ActedMessage[],
    directory: MailFolderDirectory | null,
    chosen: string | null,
): readonly { readonly storedEmailId: string; readonly destinationFolder: string }[] {
    const filed: { storedEmailId: string; destinationFolder: string }[] = [];

    for (const message of messages) {
        const destination =
            act === 'move' ? chosen : act === 'archive' || act === 'delete' ? folderOf(act, directory, message) : null;

        if (destination !== null) {
            filed.push({ storedEmailId: message.storedEmailId, destinationFolder: destination });
        }
    }

    return filed;
}

/** The folder an archive or a delete puts one message in, which is that message's own account's. */
function folderOf(
    act: 'archive' | 'delete',
    directory: MailFolderDirectory | null,
    message: ActedMessage,
): string | null {
    return folderWithRole(directory, message.account, act === 'archive' ? 'Archive' : 'Trash');
}

function everyAccountHas(
    directory: MailFolderDirectory | null,
    messages: readonly ActedMessage[],
    role: MailFolderRole,
): boolean {
    return accountsAmong(messages).every((account) => folderWithRole(directory, account, role) !== null);
}

function foldersOf(directory: MailFolderDirectory | null, account: string): readonly MailFolder[] {
    return directory?.accounts.find((entry) => entry.account.id === account)?.folders ?? [];
}

// What a folder is called: its place on the server, deepest level last, and its alias where nothing has bound one yet.
// The whole path rather than its last level, because two folders under different parents share a leaf name and a
// dialog offering both would be asking somebody to pick between two identical rows.
function nameOf(folder: MailFolder): string {
    return folder.path.length === 0 ? folder.alias : folder.path.join(' / ');
}
