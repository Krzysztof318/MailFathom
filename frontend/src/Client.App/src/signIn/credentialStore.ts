// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

    keep(deployment: DeploymentAddress, authorization: string): Promise<void>;

    forget(deployment: DeploymentAddress): Promise<void>;
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

        keep: (deployment, authorization) => {
            writeStorage(entryFor(deployment), authorization);

            return Promise.resolve();
        },

        forget: (deployment) => {
            removeStorage(entryFor(deployment));

            return Promise.resolve();
        },
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
            await shellAnswers('keep_credential', { deployment: deployment.baseAddress, authorization });
        },

        forget: async (deployment) => {
            await shellAnswers('forget_credential', { deployment: deployment.baseAddress });
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

function writeStorage(entry: string, value: string): void {
    try {
        window.sessionStorage.setItem(entry, value);
    } catch {
        // A browser configured to refuse storage still runs the client; the credential then lasts until the screen is
        // reloaded rather than until the tab closes, which is a smaller loss than a client that fails to sign in.
    }
}

function removeStorage(entry: string): void {
    try {
        window.sessionStorage.removeItem(entry);
    } catch {
        // Storage that refuses a write refuses a removal too, and there is then nothing kept in it to remove.
    }
}
