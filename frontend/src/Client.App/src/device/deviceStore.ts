// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Where the client keeps what belongs to this machine rather than to the person reading on it: the theme it is painted
// in, the language it reads in, and how the Mail space divides its width. One module, so the handling that storage a
// browser or a WebView refuses needs is written once instead of once per caller, and so the names those values are
// written under are declared together rather than as strings spread across the screens that read them.
//
// Which of the client's two stores a new setting belongs in is not decided here: `frontend/src/AGENTS.md` § *State*
// holds the one rule and the two exceptions granted to it, both of which are values above.
//
// Reached as `window.localStorage` rather than as the bare global on purpose, and this is where that reason is
// written down: Node publishes a `localStorage` global of its own, which is unavailable unless the process was started
// with `--localstorage-file`, and it wins over the one the test environment's document carries — so the bare name is
// the runtime's under Vitest and the document's in a browser, which is two different objects behind one identifier.
// The two modules that still reach a browser store directly, `deployment/adoptedDeployment.ts` and
// `workspace/rememberedWorkspace.ts`, point here for it.

/** Where a value the client keeps on the device is written. Nothing outside this module spells one of these. */
export const deviceKeys = {
    themeChoice: 'mailfathom.theme',
    locale: 'mailfathom.locale',
} as const;

/** Reading a value the device holds, writing one, and removing one. */
export interface DeviceStore {
    /** What is held under this key, or `null` where nothing is. */
    read(key: string): string | null;

    write(key: string, value: string): void;

    remove(key: string): void;
}

/**
 * The store this run keeps device-local values in.
 *
 * Which implementation answers follows what the system offers rather than which system it is. The web head and the
 * desktop head on either Linux or Windows all reach the same origin storage through the WebView they render in, so all
 * three resolve to it today; a system that refuses it — a browser configured to, a WebView started without it — falls
 * back to one that lasts the run, so the client still mounts and a value then lasts the session rather than outliving
 * it. That is the seam a head diverging later is added behind, and it is asked as a capability rather than as a
 * platform because `frontend/src/AGENTS.md` § *The two heads* refuses a module chosen by target.
 */
export function deviceStore(): DeviceStore {
    return storageOffered() ? keptOnTheDevice() : keptForTheRun();
}

/**
 * What the width of the message list is written under, which names the person rather than the machine: two people
 * sharing a machine each get their own split, and neither loses theirs by signing out.
 *
 * The name is folded to a digest rather than written out. A device store outlives the session, so a legible key would
 * leave a list of who reads mail on this machine behind for anything that can read the origin's storage.
 *
 * What is kept is an identity the person typed rather than anything the credential answers, which is why ADR 0023's
 * refusal to keep anything derived from the credential is not what this is: no secret, no stand-in for one, and
 * nothing that says whether somebody is signed in. It is a name for a pane width, and the digest is what keeps even
 * the identity out of the store.
 *
 * **The digest is not a cryptographic one**, because this is read before the first paint and `SubtleCrypto` only
 * answers asynchronously. It keeps the name out of the store rather than resisting somebody who already holds both;
 * a cryptographic one is the upgrade if a value under one of these keys ever becomes worth more than a pane width.
 */
export function listWidthKey(person: string): string {
    return `mailfathom.listWidth.${digestOf(person)}`;
}

/**
 * What the last telemetry answer the deployment gave is written under, which names the person for the same reason the
 * width above does — and here it is a privacy obligation rather than a convenience.
 *
 * Two people sharing a machine hold two different answers, and one of them is a refusal. A single name for both would
 * hand the second person the first person's answer for the length of one read, which is exactly long enough to record
 * and export something they had declined; a name per person cannot, because a key nobody has written reads as nothing
 * chosen and the unset answer is the deployment's own.
 *
 * It is the one value in this module that is not the device's own decision. The deployment holds the answer and this is
 * a cache of it, read only until that person's own read comes back and written by nothing but that read and the switch
 * itself. What it buys is the seconds between a client opening and the deployment answering, in which a client that had
 * been told no would otherwise record and export again.
 *
 * The digest and its limits are the width's above, and so is what it keeps out of the store.
 */
export function telemetryKey(person: string): string {
    return `mailfathom.telemetry.${digestOf(person)}`;
}

/** Whether this system has origin storage at all, which is the whole of what decides the implementation today. */
function storageOffered(): boolean {
    try {
        // Reaching the property is what throws where storage is refused, so the read is the probe and its answer is
        // discarded. A key nothing writes keeps the probe from depending on what happens to be stored.
        window.localStorage.getItem('mailfathom.storage');

        return true;
    } catch {
        return false;
    }
}

/** The values kept where the next start of either head reads them back. */
function keptOnTheDevice(): DeviceStore {
    return {
        read: (key) => {
            try {
                return window.localStorage.getItem(key);
            } catch {
                return null;
            }
        },

        write: (key, value) => {
            try {
                window.localStorage.setItem(key, value);
            } catch {
                // Storage found at the probe and refusing this write is a full quota or a permission withdrawn while
                // the client is open. What was chosen then lasts as long as the screen holding it, which is a smaller
                // loss than a client that fails over a preference.
            }
        },

        remove: (key) => {
            try {
                window.localStorage.removeItem(key);
            } catch {
                // Storage that refuses a removal refused the write that would have put something there.
            }
        },
    };
}

// Held for one run of the client, in memory. Module state rather than per-store, so the two calls a screen makes to
// `deviceStore` on a system without storage read each other back instead of answering out of two empty maps.
const heldForTheRun = new Map<string, string>();

/** The values kept for as long as the client is open, where the system holds nothing between starts. */
function keptForTheRun(): DeviceStore {
    return {
        read: (key) => heldForTheRun.get(key) ?? null,

        write: (key, value) => {
            heldForTheRun.set(key, value);
        },

        remove: (key) => {
            heldForTheRun.delete(key);
        },
    };
}

/** FNV-1a over the name's code units, which is a stable short name for one person rather than a secure one. */
function digestOf(text: string): string {
    let hash = 0x811c9dc5;

    for (let index = 0; index < text.length; index += 1) {
        hash ^= text.charCodeAt(index);
        hash = Math.imul(hash, 0x01000193);
    }

    return (hash >>> 0).toString(16);
}
