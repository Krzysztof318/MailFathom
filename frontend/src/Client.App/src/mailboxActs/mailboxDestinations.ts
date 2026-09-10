// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFolder, MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { folderRoleLabels, roleRank } from '../workspace/mailScope';
import { changesAFlag, type ActedMessage, type MailboxAct } from './useMailboxActs';

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

    /**
     * The role the folder plays, or `null` where its configuration labels it with none.
     *
     * Where it has one, the role is what names the folder on the screen and `name` is not drawn at all — an inbox is
     * an inbox whatever the provider called the folder and in whatever language, which is the rule the folder column
     * already draws by. Carried rather than resolved here because the name it stands for is a translation, and this
     * module composes values rather than words.
     */
    readonly role: MailFolderRole | null;
}

/**
 * The destinations of one account, which is how the design project draws the choice.
 *
 * A move stays inside the account the message is in, so exactly one group is offered today; the grouping is what says
 * *which mailbox these folders belong to* rather than a promise that two of them could be offered at once.
 */
export interface MoveDestinationGroup {
    readonly accountId: string;

    /** What the mailbox is called, which is the name its own configuration gives it. */
    readonly accountName: string;

    /**
     * Which group the account is among the user's, counted the way the folder column counts them, which is what
     * colours its mark. Everything a reader tells one mailbox from another by is that colour, so it is read from the
     * whole directory rather than from the one account offered here — the same mailbox carries the same hue wherever
     * it appears, and an ordinal taken from a list of one would recolour it inside this dialog.
     */
    readonly ordinal: number;

    readonly destinations: readonly MoveDestination[];
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

/**
 * What one destination is called on the screen: the role it plays where it plays one, and its place on the server
 * otherwise.
 *
 * One rule rather than one per screen, because the dialog offering the folder and the toast reporting the move name
 * the same folder — and a reader told they filed into *Archive* by one and into *INBOX.Archiwum* by the other has been
 * told about two folders.
 */
export function destinationName(destination: MoveDestination, translate: (key: MessageKey) => string): string {
    return destination.role === null ? destination.name : translate(folderRoleLabels[destination.role]);
}

/** The folder an account labels with that role, or `null` where its configuration labels none. */
export function folderWithRole(
    directory: MailFolderDirectory | null,
    account: string,
    role: MailFolderRole,
): string | null {
    return foldersOf(directory, account).find((folder) => folder.role === role)?.alias ?? null;
}

/**
 * Whether deleting these messages destroys them rather than filing them, which decides both the grant it is reached
 * under and the question standing in front of it.
 *
 * True exactly where every one of them is already in its own account's trash. *Delete* is one control with one symbol,
 * and what it means is read off where the mail already is — filing a message into the folder it is in would be answered
 * `already-in-destination` and change nothing, so a trash that could not be emptied would be a control that does
 * nothing wherever it is most likely to be pressed.
 *
 * Every one rather than any: a selection reaching outside the trash — from a search, say — is one press that would
 * otherwise mean two different acts at once, and the reversible reading of it is the one to take. Those already in the
 * trash are then answered as already there, which is what the deployment says about them today.
 */
export function deletesPermanently(directory: MailFolderDirectory | null, messages: readonly ActedMessage[]): boolean {
    return (
        directory !== null &&
        messages.length > 0 &&
        messages.every((message) => folderWithRole(directory, message.account, 'Trash') === message.folder)
    );
}

/** The accounts the named messages are in, each named once. */
export function accountsAmong(messages: readonly ActedMessage[]): readonly string[] {
    return [...new Set(messages.map((message) => message.account))];
}

/**
 * The folders the named messages could be filed into, grouped under the account they belong to.
 *
 * That is the one account the messages are all in: a message moves between folders of its own account and nowhere
 * else, and a selection spanning two of them is refused before this is asked. So the answer is at most one group, and
 * it is a group rather than a bare list because what a reader has to know first is which mailbox they are filing
 * inside — the same thing the folder column says with the account's name and its colour above every folder it holds.
 *
 * The folder they are already in is offered like any other: the deployment answers a message already there as its own
 * outcome, so leaving it out would be this client deciding what a folder holds from a list it read minutes ago.
 */
export function destinationsFor(
    directory: MailFolderDirectory | null,
    messages: readonly ActedMessage[],
): readonly MoveDestinationGroup[] {
    const accounts = accountsAmong(messages);
    const [only] = accounts;

    if (accounts.length !== 1 || only === undefined || directory === null) {
        return [];
    }

    const at = directory.accounts.findIndex((entry) => entry.account.id === only);
    const entry = directory.accounts[at];

    if (entry === undefined) {
        return [];
    }

    return [
        {
            accountId: entry.account.id,
            accountName: entry.account.displayName,

            // The folder column draws a row for every mailbox at once above the accounts wherever there is more than
            // one, and counts its groups from that row. Counted the same way here so that a mailbox keeps its hue
            // across the two screens.
            ordinal: directory.accounts.length > 1 ? at + 1 : at,
            destinations: entry.folders
                .map((folder) => ({ alias: folder.alias, name: nameOf(folder), role: folder.role }))
                .sort(byOfferedOrder),
        },
    ];
}

/**
 * Why the act cannot be performed on those messages, or `null` where it can.
 *
 * @param act What is being asked for.
 * @param messages The messages it would be about.
 * @param directory The user's folders, or `null` where they have not been read.
 * @param offered Whether the credential may write the flags an act needs, move mail, and delete it.
 */
export function refusalFor(
    act: MailboxAct,
    messages: readonly ActedMessage[],
    directory: MailFolderDirectory | null,
    offered: { readonly flags: boolean; readonly moves: boolean; readonly deletes: boolean },
): ActRefusal | null {
    if (messages.length === 0) {
        return 'nothingToActOn';
    }

    if (changesAFlag(act)) {
        return offered.flags ? null : 'notOffered';
    }

    // The three acts below are folder moves, and folders this client has not read are not folders an account does not
    // have. Said apart for that reason: a read that failed and a mailbox labelling no archive would otherwise reach a
    // reader as the same sentence, and only one of the two is something they can do anything about.
    //
    // A credential holding neither grant is told that instead, because the folders are not read at all without one:
    // `foldersUnknown` would name a read nobody was ever going to make, and offer a second attempt at it.
    if (directory === null) {
        return offered.moves || offered.deletes ? 'foldersUnknown' : 'notOffered';
    }

    // Read before the moving grant, because a delete in the trash is not reached under it: a credential that may
    // destroy mail and not file it can still empty the trash, and one that may file mail and not destroy it is told so
    // here rather than by the route refusing what a control had already offered.
    if (act === 'delete' && deletesPermanently(directory, messages)) {
        return offered.deletes ? null : 'notOffered';
    }

    if (!offered.moves) {
        return 'notOffered';
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
    return destinationsFor(directory, messages).some((group) =>
        group.destinations.some((destination) => messages.some((message) => message.folder !== destination.alias)),
    );
}

// The order the folders are offered in, which is the folder column's own: the roles first, in the order a mail client
// has shown them in for thirty years, and everything else after them by name. Two screens listing one account's
// folders in two different orders would be two mailboxes as far as a reader is concerned.
function byOfferedOrder(one: MoveDestination, other: MoveDestination): number {
    const byRole = roleRank(one.role) - roleRank(other.role);

    return byRole === 0 ? one.name.localeCompare(other.name) : byRole;
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
