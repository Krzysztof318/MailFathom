// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { DeploymentAddress } from '@mailfathom/client-backend';

// Where what this client signed in with is kept between starts, as decided by ADR 0023. What is stored is the session
// document `keptSession.ts` writes — the finished header value, the instant it stops working, and the name of the
// person it belongs to — as one value bound to the address it was given for. The name travels beside the credential
// because a session token names nobody: the Basic header it replaced carried the user name inside it, and a bearer
// token is a value with no reader.
//
// **A session is kept in one of two places, and the person picks which.** *Keep me signed in* on the sign-in screen is
// what picks it, and the two places are the same two on every head: the tab, which is `sessionStorage` and dies with
// it, and the device, which is whatever durable place the head actually offers. That is the amendment ADR 0023 records:
// the durable place was refused outright on the web head until somebody could ask for it, and asking for it is what the
// checkbox is. Nothing is kept durably that was not asked for, which is the property the refusal was protecting.
//
// The application depends on the three operations below and on one thing they report — where a session asked to
// outlive the tab would actually go — and never on which head it was handed. Which durable half is constructed is
// decided once, by `credentialStore` below, from the arrangement a shell said it offers, and no screen underneath asks
// which head it is running on.
//
// A shell states that arrangement rather than a fact about its machine, because ADR 0027 decided that the same fact —
// protected storage this client cannot reach — resolves one way where the page is the only other place to keep a
// credential and the other way where the platform kills the client constantly. Only the shell knows which of those it is,
// so it answers with the arrangement itself and nothing here has to learn a platform's name to render the right
// sentence.

/**
 * Where a session somebody asked to keep beyond the tab is actually put, which is a sentence the sign-in screen renders
 * before anybody types.
 *
 * The last two are not places so much as the absence of one, and they carry their reason because that is the half a
 * person can act on: a store that would have kept the sign-in and could not be reached is a different sentence from one
 * whose key the operating system threw away. Where it is one of those two, the screen offers nothing to tick — there is
 * nothing on the other side of the choice — and says why the sign-in will not outlive this run.
 */
export type KeptBeyondTheTab =
    | 'inTheDeviceStore'
    | 'inThisBrowser'
    | 'nowhereTheShellKeepsTheRun'
    | 'nowhereStorageUnreachable'
    | 'nowhereKeyInvalidated';

/** Whether a place this client could keep a session in exists at all, which is what decides that the choice is offered. */
export function offersMoreThanTheTab(kept: KeptBeyondTheTab): boolean {
    return kept === 'inTheDeviceStore' || kept === 'inThisBrowser';
}

/** Where the credential lives between starts: keep it, read it back, forget it, and say what outliving the tab means. */
export interface CredentialStore {
    readonly beyondTheTab: KeptBeyondTheTab;

    /** The credential kept for this deployment, wherever it was put, or `null` where none was kept for it. */
    read(deployment: DeploymentAddress): Promise<string | null>;

    /**
     * Keeps this credential for that deployment, answering whether it is stored.
     *
     * `beyondTheTab` is the person's own choice, and it is passed at sign-in and omitted afterwards. Omitted, the
     * session is written back wherever the one before it was kept — which is what a renewal is doing: replacing a
     * session rather than taking the choice again. A renewal that had to be told would mean carrying the answer through
     * every screen between the checkbox and the timer, and getting it wrong there would silently move somebody's
     * session out of the store they picked.
     *
     * A session lives in exactly one of the two places, so writing it to one removes it from the other. That matters in
     * the direction nobody would test: somebody who signs out of a kept session and back in without ticking the box has
     * asked for the durable copy to be gone, and a write that only added would leave it there to be read at the next
     * start.
     *
     * `false` is a store that would not write — a keychain locked between being found and being written to, a browser
     * that stopped permitting storage, a device whose protected storage keeps nothing — and it is answered for the
     * same reason `forget` answers: the screen has already told the person how long what it keeps will last, so a
     * refused write that reported nothing would leave them asked for the password again at the next start with
     * nothing having said why.
     */
    keep(deployment: DeploymentAddress, credential: string, beyondTheTab?: boolean): Promise<boolean>;

    /**
     * Removes what was kept for this deployment, from both places, answering whether it is gone.
     *
     * `false` is a store that would not delete — a locked keychain, a Secret Service that stopped answering — and it
     * has to be answered rather than swallowed: the screen has already promised that signing out is what removes the
     * sign-in, so a refused deletion that reported nothing would leave the credential in the store for the next start
     * to read back while the person believes they signed out.
     */
    forget(deployment: DeploymentAddress): Promise<boolean>;
}

/** The durable half of a store — the place a session goes when somebody asked for it to outlive the tab. */
interface DeviceStore {
    readonly beyondTheTab: KeptBeyondTheTab;

    /**
     * Whether the page's own per-tab storage may hold a session on this head at all.
     *
     * `false` on the one head ADR 0027 wrote it for: a device whose protected storage was there and could not be
     * reached keeps nothing anywhere, because the client on it is killed and restarted all day and a credential in the
     * page is then readable by anything reaching the origin for far longer than a tab ever lasts. It is a property of
     * the device half rather than a fourth `KeptBeyondTheTab` value, because it answers a different question — where a
     * session goes when nobody asked for one to be kept.
     */
    readonly theTabMayKeepIt: boolean;

    read(deployment: DeploymentAddress): Promise<string | null>;

    keep(deployment: DeploymentAddress, credential: string): Promise<boolean>;

    forget(deployment: DeploymentAddress): Promise<boolean>;
}

/**
 * The store this run keeps its credential in.
 *
 * There is no shell on the web head, and the page's own storage is what is left — `localStorage` for a session somebody
 * asked to keep and `sessionStorage` for one they did not. A shell answers with the arrangement it offers: the
 * operating system's protected store where it has one, the page's own storage where it has none and that is the safer
 * of the two remaining answers, and neither where it has one it could not reach — which keeps nothing durable rather
 * than writing a credential to a page on a device that is killed and restarted all day.
 *
 * A shell that answers with something this client cannot read — an arrangement it does not know, or a command that
 * refused — keeps nothing durable, which is the only answer that is safe on both heads. Reading it as the page's own
 * storage instead would put the credential there on a device ADR 0027 refuses that for, and the client cannot tell
 * whether the device it is on is one of those; where the answer is unreadable, so is the head.
 */
export async function credentialStore(): Promise<CredentialStore> {
    const shell = window.__TAURI__;

    if (shell === undefined) {
        return storeOver(keptInThisBrowser());
    }

    const arrangement = await shell.core.invoke('credential_arrangement').catch(() => null);

    switch (arrangement) {
        case 'keptInTheStore':
            return storeOver(keptInTheProtectedStore());
        case 'keptForTheRun':
            // The shell asked for the run and not beyond it, which is the answer ADR 0027 wrote for a device that
            // kills the client all day. So there is no durable half to offer here and nothing to tick: the page's own
            // `localStorage` is exactly what that arrangement is refusing, and reading the checkbox as permission to
            // use it would let a screen overrule the shell that knows the device.
            return storeOver(keptNowhere('nowhereTheShellKeepsTheRun'));
        case 'notKeptKeyInvalidated':
            return storeOver(keptNowhere('nowhereKeyInvalidated'));
        default:
            return storeOver(keptNowhere('nowhereStorageUnreachable'));
    }
}

/**
 * One store over the two places, which is where the choice between them is actually made.
 *
 * The tab half is the same code on every head — `sessionStorage` is a browser API and both heads are browsers — so it
 * is written once here rather than into each durable half beside a place that has nothing to do with it.
 */
function storeOver(device: DeviceStore): CredentialStore {
    return {
        beyondTheTab: device.beyondTheTab,

        // The device first, because that is where a session outliving the tab is, and the tab's own copy is what a
        // person who did not ask for one has. Only one of the two ever holds an entry for a deployment, so the order
        // decides nothing about correctness; it decides which read is made in the case that has one.
        read: async (deployment) =>
            (await device.read(deployment)) ?? (device.theTabMayKeepIt ? readStorage(entryFor(deployment)) : null),

        keep: async (deployment, credential, beyondTheTab) => {
            // What the device is holding already, which answers two questions in one read: whether a renewal that was
            // told nothing is replacing a kept session or a tab's own, and whether declining to keep one has anything
            // to remove. Asking a store to forget what it never held is a shell command per sign-in on the head that
            // has a shell, which is why the second question is asked rather than assumed.
            const held = await device.read(deployment);
            const keepOnTheDevice = beyondTheTab ?? held !== null;

            if (!keepOnTheDevice) {
                if (held !== null) {
                    await device.forget(deployment);
                }

                return keptInTheTab(device, deployment, credential);
            }

            const stored = await device.keep(deployment, credential);

            // A device that refused the write leaves the session in the tab rather than nowhere, because somebody who
            // ticked the box is still signed in for this tab and the alternative is losing a session that works. What
            // is not done is reporting it as kept: the answer below is the device's, so the screen says the sign-in was
            // not kept and the person is not promised a tomorrow they will not get. On the head that keeps nothing in
            // the page, there is no such consolation and the session lives in memory for this run alone.
            removeStorage(entryFor(deployment));

            if (!stored) {
                keptInTheTab(device, deployment, credential);
            }

            return stored;
        },

        forget: async (deployment) => {
            // Both, and the device's answer is what is reported: a tab copy removed while a durable one survives is a
            // sign-out that removed the half nobody was worried about.
            const deviceForgot = await device.forget(deployment);

            return removeStorage(entryFor(deployment)) && deviceForgot;
        },
    };
}

/** The session put where this tab can read it back, on the heads where the page is a place a session may be kept. */
function keptInTheTab(device: DeviceStore, deployment: DeploymentAddress, credential: string): boolean {
    return device.theTabMayKeepIt && writeStorage(entryFor(deployment), credential);
}

/** What the credential is written under, which names the deployment so a credential is never read back for another. */
function entryFor(deployment: DeploymentAddress): string {
    return `mailfathom.credential.${deployment.baseAddress}`;
}

/**
 * The device half on a head with no shell behind it, which is the page's own `localStorage`.
 *
 * ADR 0023 refused this outright until [#1844](https://github.com/Krzysztof318/MailFathom/issues/1844), and the
 * reasoning it was refused under is unchanged rather than overturned: `localStorage` is readable by any script that
 * reaches the origin and it outlives the tab and the browser, so a script injected next month reads a session stored
 * today. What changed is who decides to take that: it is written only where somebody ticked *Keep me signed in*, and a
 * person signing in on a machine they share leaves it alone and is kept for the tab exactly as before.
 */
function keptInThisBrowser(): DeviceStore {
    return {
        beyondTheTab: 'inThisBrowser',
        theTabMayKeepIt: true,

        read: (deployment) => Promise.resolve(readDeviceStorage(entryFor(deployment))),

        keep: (deployment, credential) => Promise.resolve(writeDeviceStorage(entryFor(deployment), credential)),

        forget: (deployment) => Promise.resolve(removeDeviceStorage(entryFor(deployment))),
    };
}

/**
 * The device half on a head whose shell has protected storage it could not reach, which keeps nothing.
 *
 * `keep` answers `false` because nothing was stored and the screen has to say so at the moment somebody signs in, and
 * `read` answers nothing because nothing this run wrote can be read back.
 *
 * `forget` still asks the shell, which is the half that is not symmetrical with the other two. This arrangement says
 * the store could not be reached *this run*, never that it holds nothing: a run whose store opened normally may have
 * written a credential that is still there, and removing it needs no key on any head — so answering `true` here would
 * report a sign-out that removed nothing and leave the credential to be read back by the next run that can open the
 * store.
 */
function keptNowhere(
    beyondTheTab: 'nowhereTheShellKeepsTheRun' | 'nowhereStorageUnreachable' | 'nowhereKeyInvalidated',
): DeviceStore {
    return {
        beyondTheTab,

        // The shell that keeps only the run is saying the page is where a session goes for that run, which is what
        // this arrangement has always meant. The other two are saying the opposite about a device that is not safe to
        // leave one on, so nothing is kept there at all.
        theTabMayKeepIt: beyondTheTab === 'nowhereTheShellKeepsTheRun',

        read: () => Promise.resolve(null),

        keep: () => Promise.resolve(false),

        forget: async (deployment) =>
            (await shellAnswers('forget_credential', { deployment: deployment.baseAddress })) === true,
    };
}

/** The device half on the desktop head, which is the operating system's own protected store. */
function keptInTheProtectedStore(): DeviceStore {
    return {
        beyondTheTab: 'inTheDeviceStore',
        theTabMayKeepIt: true,

        read: async (deployment) => {
            const kept = await shellAnswers('read_credential', { deployment: deployment.baseAddress });

            return typeof kept === 'string' ? kept : null;
        },

        keep: async (deployment, credential) => {
            return (
                (await shellAnswers('keep_credential', {
                    deployment: deployment.baseAddress,
                    credential,
                })) === true
            );
        },

        forget: async (deployment) => {
            // The shell's own answer, because a deletion nobody performed is a credential left on the device: the entry
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

function readDeviceStorage(entry: string): string | null {
    try {
        return window.localStorage.getItem(entry);
    } catch {
        return null;
    }
}

function writeDeviceStorage(entry: string, value: string): boolean {
    try {
        window.localStorage.setItem(entry, value);

        return true;
    } catch {
        return false;
    }
}

function removeDeviceStorage(entry: string): boolean {
    try {
        window.localStorage.removeItem(entry);

        return true;
    } catch {
        return true;
    }
}
