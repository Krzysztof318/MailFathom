// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { DeploymentAddress } from '@mailfathom/client-backend';

// Where the credential this client signed in with is kept between starts, as decided by ADR 0023. What is stored is the
// finished header value, one value, bound to the address it was given for; the user name is inside it already and
// nothing derived from it is kept beside it.
//
// The application depends on the three operations below and on one thing it reports — how long what it keeps survives —
// and never on which of them it was handed. Which one is constructed is decided once, by `credentialStore` below, from
// the arrangement a shell said it offers, and no screen underneath asks which head it is running on.
//
// A shell states that arrangement rather than a fact about its machine, because ADR 0027 decided that the same fact —
// protected storage this client cannot reach — resolves one way where the page is the only other place to keep a
// password and the other way where the platform kills the client constantly. Only the shell knows which of those it is,
// so it answers with the arrangement itself and nothing here has to learn a platform's name to render the right
// sentence.

/**
 * How long what a store keeps outlives the client, which is a sentence the sign-in screen renders before anybody types.
 *
 * The last two are not durations so much as the absence of one, and they carry their reason because that is the half a
 * person can act on: a store that would have kept the password and could not be reached is a different sentence from
 * one whose key the operating system threw away, and neither is the desktop's "this machine offers no keychain".
 */
export type CredentialLifetime =
    | 'untilSignedOut'
    | 'untilTheTabCloses'
    | 'untilTheClientCloses'
    | 'notKeptStorageUnreachable'
    | 'notKeptKeyInvalidated';

/** Where the credential lives between starts: keep it, read it back, forget it, and say how long that lasts. */
export interface CredentialStore {
    readonly lifetime: CredentialLifetime;

    /** The credential kept for this deployment, or `null` where none was kept for it. */
    read(deployment: DeploymentAddress): Promise<string | null>;

    /**
     * Keeps this credential for that deployment, answering whether it is stored.
     *
     * `false` is a store that would not write — a keychain locked between being found and being written to, a browser
     * that stopped permitting storage, a device whose protected storage keeps nothing — and it is answered for the
     * same reason `forget` answers: the screen has already told the person how long what it keeps will last, so a
     * refused write that reported nothing would leave them asked for the password again at the next start with
     * nothing having said why.
     */
    keep(deployment: DeploymentAddress, authorization: string): Promise<boolean>;

    /**
     * Removes what was kept for this deployment, answering whether it is gone.
     *
     * `false` is a store that would not delete — a locked keychain, a Secret Service that stopped answering — and it
     * has to be answered rather than swallowed: the screen has already promised that signing out is what removes the
     * password, so a refused deletion that reported nothing would leave the credential in the store for the next start
     * to read back while the person believes they signed out.
     */
    forget(deployment: DeploymentAddress): Promise<boolean>;
}

/**
 * The store this run keeps its credential in.
 *
 * There is no shell on the web head, and the page's own storage is what is left. A shell answers with the arrangement
 * it offers: the operating system's protected store where it has one, the run where it has none and the page is the
 * safer of the two remaining answers, and neither where it has one it could not reach — which keeps nothing rather
 * than writing a password to a page on a device that is killed and restarted all day.
 *
 * A shell that answers with something this client cannot read — an arrangement it does not know, or a command that
 * refused — keeps nothing, which is the only answer that is safe on both heads. Reading it as the run instead would
 * put the password in the page's own storage, and the client cannot tell whether the device it is on is one ADR 0027
 * refuses that for; where the answer is unreadable, so is the head.
 */
export async function credentialStore(): Promise<CredentialStore> {
    const shell = window.__TAURI__;

    if (shell === undefined) {
        return keptForTheRun('untilTheTabCloses');
    }

    const arrangement = await shell.core.invoke('credential_arrangement').catch(() => null);

    switch (arrangement) {
        case 'keptInTheStore':
            return keptInTheProtectedStore();
        case 'keptForTheRun':
            return keptForTheRun('untilTheClientCloses');
        case 'notKeptKeyInvalidated':
            return keptNowhere(arrangement);
        default:
            return keptNowhere('notKeptStorageUnreachable');
    }
}

/** What the credential is written under, which names the deployment so a credential is never read back for another. */
function entryFor(deployment: DeploymentAddress): string {
    return `mailfathom.credential.${deployment.baseAddress}`;
}

/**
 * The credential kept for as long as the client is open, in storage the document owns.
 *
 * `sessionStorage` rather than `localStorage`: both are readable by any script that reaches the origin, and only the
 * second outlives the tab and the browser — which would leave a password a script injected long afterwards could read,
 * with no expiry to limit it. What this buys over holding the value in memory is the reload, which a single-page
 * application meets far more often than a person expects to sign in.
 */
function keptForTheRun(lifetime: 'untilTheTabCloses' | 'untilTheClientCloses'): CredentialStore {
    return {
        lifetime,

        read: (deployment) => Promise.resolve(readStorage(entryFor(deployment))),

        keep: (deployment, authorization) => Promise.resolve(writeStorage(entryFor(deployment), authorization)),

        forget: (deployment) => Promise.resolve(removeStorage(entryFor(deployment))),
    };
}

/**
 * The credential kept nowhere at all, on a head whose shell has protected storage it could not reach.
 *
 * `keep` answers `false` because nothing was stored and the screen has to say so at the moment somebody signs in, and
 * `read` answers nothing because nothing this run wrote can be read back.
 *
 * `forget` still asks the shell, which is the half that is not symmetrical with the other two. This arrangement says
 * the store could not be reached *this run*, never that it holds nothing: a run whose store opened normally may have
 * written a credential that is still there, and removing it needs no key on any head — so answering `true` here would
 * report a sign-out that removed nothing and leave the password to be read back by the next run that can open the
 * store.
 */
function keptNowhere(lifetime: 'notKeptStorageUnreachable' | 'notKeptKeyInvalidated'): CredentialStore {
    return {
        lifetime,

        read: () => Promise.resolve(null),

        keep: () => Promise.resolve(false),

        forget: async (deployment) =>
            (await shellAnswers('forget_credential', { deployment: deployment.baseAddress })) === true,
    };
}

/** The credential kept in the operating system's own protected store, which the shell reaches and the WebView cannot. */
function keptInTheProtectedStore(): CredentialStore {
    return {
        lifetime: 'untilSignedOut',

        read: async (deployment) => {
            const kept = await shellAnswers('read_credential', { deployment: deployment.baseAddress });

            return typeof kept === 'string' ? kept : null;
        },

        keep: async (deployment, authorization) => {
            return (
                (await shellAnswers('keep_credential', {
                    deployment: deployment.baseAddress,
                    authorization,
                })) === true
            );
        },

        forget: async (deployment) => {
            // The shell's own answer, because a deletion nobody performed is a password left on the device: the entry
            // outlives uninstalling the application.
            return (await shellAnswers('forget_credential', { deployment: deployment.baseAddress })) === true;
        },
    };
}

/**
 * What the shell made of one command, or `null` where it refused or was not there.
 *
 * A store that will not answer leaves the client asking for the password again, which is the same outcome a browser
 * refusing storage produces and is a smaller loss than a client that fails to open over it. Nothing is reported out of
 * here, because everything that could be reported is about a value this module exists to keep quiet.
 */
async function shellAnswers(command: string, argument: Record<string, unknown>): Promise<unknown> {
    try {
        return await window.__TAURI__?.core.invoke(command, argument);
    } catch {
        return null;
    }
}

function readStorage(entry: string): string | null {
    try {
        return window.sessionStorage.getItem(entry);
    } catch {
        return null;
    }
}

/** Whether the value is stored, which a browser configured to refuse storage answers `false` rather than throwing on. */
function writeStorage(entry: string, value: string): boolean {
    try {
        window.sessionStorage.setItem(entry, value);

        return true;
    } catch {
        // A browser configured to refuse storage still runs the client, and signing in still worked: the credential
        // then lasts until the screen is reloaded rather than until the tab closes. What is owed is telling somebody
        // that, which is why this is answered rather than swallowed here.
        return false;
    }
}

/** Whether the entry is gone, which storage that refused every write answers as truthfully as one that held it. */
function removeStorage(entry: string): boolean {
    try {
        window.sessionStorage.removeItem(entry);

        return true;
    } catch {
        // Storage that refuses a removal refused the write that would have put something there, so nothing is kept
        // under this name either way — which is the outcome asked for rather than a failure to report.
        return true;
    }
}
