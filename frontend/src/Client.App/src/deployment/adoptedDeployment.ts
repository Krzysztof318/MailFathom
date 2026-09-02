// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { resolveDeploymentEntry, type DeploymentAddress } from '@mailfathom/client-backend';

// Which deployment this run of the client belongs to, resolved once at the application's edge and handed to it as a
// value. Nothing here asks which head it is running on, and nothing anywhere else does either: what the two heads
// differ in is where an address comes from, and both answers are addresses.
//
// The web bundle is served by the deployment itself, so the origin that served the page is the deployment and there is
// nothing to ask anybody. A desktop shell is loaded from a scheme of its own that no deployment ever answers on, so
// the origin resolves to nothing there and what is left is the address somebody gave — which is why neither case
// needs to know about the other.

/** What the client is pointed at, and whether a person is the one who pointed it. */
export interface AdoptedDeployment {
    readonly deployment: DeploymentAddress;

    /**
     * Whether somebody named this deployment, rather than it being the origin that served the client.
     *
     * It is what decides whether the client offers to be pointed somewhere else: an address a person gave is theirs to
     * change, and an origin that served the page is not something changing an address could move.
     */
    readonly chosen: boolean;
}

// Where the chosen address is written. An address is not a credential and is stored as what it is; what the desktop
// head does with anything secret is its own question.
//
// Reached as `window.localStorage` rather than as the bare global for the reason `localization/locale.ts` gives: Node
// publishes a `localStorage` global of its own that wins over the document's under the test runner, so the bare name
// is two different objects behind one identifier.
const storageKey = 'mailfathom.deployment';

/** The deployment this run belongs to, or `null` where nobody has said and nothing served the client from one. */
export function adoptedDeployment(): AdoptedDeployment | null {
    const chosen = chosenDeployment();
    if (chosen !== null) {
        return { deployment: chosen, chosen: true };
    }

    const serving = servingDeployment();

    return serving === null ? null : { deployment: serving, chosen: false };
}

/** Remembers the deployment somebody named, so the next start of either head opens against it. */
export function storeDeployment(deployment: DeploymentAddress): void {
    try {
        window.localStorage.setItem(storageKey, deployment.baseAddress);
    } catch {
        // A browser configured to refuse storage still runs the client; the address then lasts the run rather than
        // outliving it, which is a smaller loss than a client that fails to start over a preference.
    }
}

/** Forgets the deployment somebody named, which is how the client is pointed somewhere else. */
export function forgetDeployment(): void {
    try {
        window.localStorage.removeItem(storageKey);
    } catch {
        // Storage that refuses a write refuses a removal too, and the address it is holding is one this run has
        // already stopped using.
    }
}

/** The address somebody named on this machine, or `null` where none was named or what was stored is not an address. */
function chosenDeployment(): DeploymentAddress | null {
    let stored: string | null;

    try {
        stored = window.localStorage.getItem(storageKey);
    } catch {
        return null;
    }

    if (stored === null) {
        return null;
    }

    // Read back through the same rule that let it be written, permitting clear text: an address only reaches storage
    // by being resolved, and a clear-text one only by somebody declaring it there. What this run is checking is that
    // what came back is still an address at all.
    const resolved = resolveDeploymentEntry(stored, true);

    return resolved.outcome === 'resolved' ? resolved.deployment : null;
}

/** The deployment that served this client, or `null` where whatever served it is not one the client could address. */
function servingDeployment(): DeploymentAddress | null {
    const serving = import.meta.env.VITE_MAILFATHOM_SERVICE_ADDRESS ?? window.location.origin;
    const resolved = resolveDeploymentEntry(serving, false);

    return resolved.outcome === 'resolved' ? resolved.deployment : null;
}
