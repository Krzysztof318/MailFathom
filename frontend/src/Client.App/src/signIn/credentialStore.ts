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
// whether a shell is there to keep it in a keychain, and no screen underneath asks which head it is running on.

/** How long what a store keeps outlives the client, which is a sentence the sign-in screen renders before anybody types. */
export type CredentialLifetime = 'untilSignedOut' | 'untilTheTabCloses' | 'untilTheClientCloses';

/** Where the credential lives between starts: keep it, read it back, forget it, and say how long that lasts. */
export interface CredentialStore {
    readonly lifetime: CredentialLifetime;

    /** The credential kept for this deployment, or `null` where none was kept for it. */
    read(deployment: DeploymentAddress): Promise<string | null>;

    /**
     * Keeps this credential for that deployment, answering whether it is stored.
     *
     * `false` is a store that would not write — a keychain locked between being found and being written to, a browser
     * that stopped permitting storage — and it is answered for the same reason `forget` answers: the screen has
     * already told the person how long what it keeps will last, so a refused write that reported nothing would leave
     * them asked for the password again at the next start with nothing having said why.
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
 * A shell that answers is one offering an operating-system keychain, and a machine whose keychain cannot be reached
 * keeps the credential for the run exactly as a browser tab does — which is a supported outcome with wording of its
 * own rather than a silent fallback to a worse store.
 */
export async function credentialStore(): Promise<CredentialStore> {
    const shell = window.__TAURI__;

    if (shell === undefined) {
        return keptForTheRun('untilTheTabCloses');
    }

    const reachable = await shell.core.invoke('keychain_reachable').catch(() => false);

    return reachable === true ? keptInTheKeychain() : keptForTheRun('untilTheClientCloses');
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
function keptForTheRun(lifetime: CredentialLifetime): CredentialStore {
    return {
        lifetime,

        read: (deployment) => Promise.resolve(readStorage(entryFor(deployment))),

        keep: (deployment, authorization) => Promise.resolve(writeStorage(entryFor(deployment), authorization)),

        forget: (deployment) => Promise.resolve(removeStorage(entryFor(deployment))),
    };
}

/** The credential kept in the operating system's own credential store, which the shell reaches and the WebView cannot. */
function keptInTheKeychain(): CredentialStore {
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
            // The shell's own answer, because a deletion nobody performed is a password left on the machine: the entry
            // outlives uninstalling the application.
            return (await shellAnswers('forget_credential', { deployment: deployment.baseAddress })) === true;
        },
    };
}

/**
 * What the shell made of one command, or `null` where it refused or was not there.
 *
 * A keychain that will not answer leaves the client asking for the password again, which is the same outcome a browser
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
