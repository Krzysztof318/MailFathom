// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What the person changes about their own folders, which is a change to what this deployment is *configured* to read
// rather than a change to a mailbox. Nothing here reaches a mail server: a folder becomes a mapping in the user's own
// record, the account's next run resolves it, and the mapping is what says the server may be asked to create it.
//
// That is why these operations sit apart from `mailFolders.ts`, which answers what the folders *are*. Reading the
// tree and stating what it should be are two surfaces on the service, and the client keeps them two modules for the
// same reason it keeps them two routes.
//
// **Every write states the version it was composed over**, which the service refuses when somebody else has moved
// past it. So a caller reads the version once and carries the one each write answers with, and a refusal is a value
// it can act on rather than a failure — it says the record moved, and reading it again is what to do about that.

/** The route the acting user's own record is read at, relative to the client prefix. */
export const userRecordRoute = '/record';

/** The route one folder is declared at. */
export const folderDeclarationRoute = `${userRecordRoute}/mail-accounts/folders`;

/** The route one folder is stated afresh at, in place of the one carrying an alias. */
export const folderReplacementRoute = `${folderDeclarationRoute}/replacement`;

/** The route one folder is withdrawn at. */
export const folderRemovalRoute = `${folderDeclarationRoute}/removal`;

// A record's own document is somebody's mailboxes and their settings, and nothing on this surface reads it — the
// version is the whole of what a folder change needs. It is still bounded, because the answer has to be received
// before the field can be taken out of it.
const longestRecordAnswer = 512 * 1024;

// A write answers what it did and the version now in force, and its messages are sentences the service composed.
const longestWriteAnswer = 32 * 1024;

/** What a folder is declared as, which is the shape a configuration file states one in. */
export interface DeclaredMailFolder {
    /** MailFathom's own name for the folder, which nests on a slash and which every other route names it by. */
    readonly alias: string;

    /** Where the folder sits on its mail server, outermost level first. */
    readonly remotePath: readonly string[];
}

/** What a write to the record did, and the version the next one is composed over. */
export interface MailFolderConfigurationOutcome {
    /** Whether the change reached the record, or was refused by a rule the person acts on. */
    readonly committed: boolean;

    /** The version now in force, which the next change states whether this one committed or not. */
    readonly version: number;

    /** What the service said about a refusal, in its own words, empty where it committed. */
    readonly messages: readonly string[];
}

/** Reads the version the acting user's record stands at, which every folder change is composed over. */
export function readFolderConfigurationVersion(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<number>> {
    return spanned(`GET ${userRecordRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, userRecordRoute),
            headers: headersFor(session),
            longestAnswer: longestRecordAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const version = versionIn(response.body);

        return version === null ? failed('unreadable', response.status) : read(version);
    });
}

/** Declares one more folder in one of the acting user's mail accounts. */
export function declareMailFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: { readonly accountId: string; readonly folder: DeclaredMailFolder; readonly version: number },
): Promise<ClientResult<MailFolderConfigurationOutcome>> {
    return writeFolder(session, transport, folderDeclarationRoute, {
        version: stated.version,
        accountId: stated.accountId,
        folder: folderDocument(stated.folder),
    });
}

/** States one folder afresh, in place of the one the account declares under an alias. */
export function replaceMailFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: {
        readonly accountId: string;
        readonly alias: string;
        readonly folder: DeclaredMailFolder;
        readonly version: number;
    },
): Promise<ClientResult<MailFolderConfigurationOutcome>> {
    return writeFolder(session, transport, folderReplacementRoute, {
        version: stated.version,
        accountId: stated.accountId,
        alias: stated.alias,
        folder: folderDocument(stated.folder),
    });
}

/** Withdraws one folder from one of the acting user's mail accounts, leaving the mail already stored out of it. */
export function withdrawMailFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: { readonly accountId: string; readonly alias: string; readonly version: number },
): Promise<ClientResult<MailFolderConfigurationOutcome>> {
    return writeFolder(session, transport, folderRemovalRoute, {
        version: stated.version,
        accountId: stated.accountId,
        alias: stated.alias,
    });
}

// The declaration a file would have written, which is the shape the service composes into the record. `CreateIfMissing`
// is what a folder somebody just named needs and what a folder somebody is repointing needs equally: the path is a
// place on their server rather than a folder that is already there, and the service is permitted to create exactly the
// folder its own configuration maps.
function folderDocument(folder: DeclaredMailFolder): string {
    return JSON.stringify({
        Alias: folder.alias,
        RemotePath: folder.remotePath.join('/'),
        CreateIfMissing: true,
    });
}

function writeFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    stated: object,
): Promise<ClientResult<MailFolderConfigurationOutcome>> {
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

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const outcome = outcomeIn(response.body);

        return outcome === null ? failed('unreadable', response.status) : read(outcome);
    });
}

function versionIn(body: string): number | null {
    const record = parsed(body);
    const version = record?.['version'];

    return isVersion(version) ? version : null;
}

function outcomeIn(body: string): MailFolderConfigurationOutcome | null {
    const record = parsed(body);

    if (record === null) {
        return null;
    }

    const committed = record['committed'];
    const version = record['version'];
    const stated = record['messages'] ?? [];

    if (typeof committed !== 'boolean' || !isVersion(version) || !Array.isArray(stated)) {
        return null;
    }

    // Bounded during the walk rather than after it, and every entry checked: these are sentences a screen renders,
    // so an answer carrying something that is not one is refused rather than drawn.
    const messages: string[] = [];
    for (const message of stated) {
        if (typeof message !== 'string' || messages.length >= mostRefusalMessages) {
            return null;
        }

        messages.push(message);
    }

    return { committed, version, messages };
}

// What a refusal can say at once. The service composes one sentence per rule it applied, and a record has a bounded
// number of rules; anything past this is an answer no deployment produced.
const mostRefusalMessages = 64;

function parsed(body: string): Readonly<Record<string, unknown>> | null {
    try {
        return asRecord(JSON.parse(body));
    } catch {
        return null;
    }
}

// A version is a whole number the row is at, so a fraction, a negative, and a value past what arithmetic here stays
// exact for are each an answer no deployment produced.
function isVersion(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
