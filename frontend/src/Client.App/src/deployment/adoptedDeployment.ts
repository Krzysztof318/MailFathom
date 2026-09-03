// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { resolveDeploymentEntry, type DeploymentAddress } from '@mailfathom/client-backend';
import type { ConfiguredConnection } from '../shellOperations/configuredConnection';

// Which deployment this run of the client belongs to, resolved once at the application's edge and handed to it as a
// value. Nothing here asks which head it is running on, and nothing anywhere else does either: what the two heads
// differ in is where an address comes from, and every answer is an address.
//
// Three of them, in this order. A deployment that configured the client says where its service is, which is the case a
// packaged client handed to somebody by their organization is in. Otherwise the address somebody named on this machine
// stands, read back so a later start opens against it. Otherwise the origin that served the page is the deployment,
// which is what a web bundle served by its own deployment is in — and that is also where the build-time value sits,
// beneath all of the above, so an orchestration naming the service for a development run cannot beat what an operator
// configured or what a person chose.
//
// A configured address is the one of the three that can be *wrong*, because the other two were resolved by this module
// before they were stored or served. So this is where a configuration that does not resolve is refused by name rather
// than dropped: an operator who mistyped an address has to learn it from the screen, and a client that silently
// ignored them would ask for an address they were told they would not have to give.

/** Where the address this run uses came from, which decides what the sign-in screen draws about it. */
export const deploymentOrigins = ['configured', 'chosen', 'serving'] as const;

export type DeploymentOrigin = (typeof deploymentOrigins)[number];

/** Why what a deployment configured is not something this client will connect to. */
export type ConfigurationRefusal =
    'addressMalformed' | 'addressNeedsClearTextPermission' | 'clearTextContradictsAddress' | 'permissionNotABoolean';

/** What the client is pointed at, and where that came from. */
export interface AdoptedDeployment {
    readonly deployment: DeploymentAddress;
    readonly origin: DeploymentOrigin;
}

/** Everything the edge resolved about the connection, or the refusal that stops this run before a password is asked for. */
export type ClientDeployment =
    | { readonly outcome: 'refused'; readonly refusal: ConfigurationRefusal }
    | {
          readonly outcome: 'resolved';

          /** The deployment this run belongs to, or `null` where nobody has said and nothing served the client from one. */
          readonly adopted: AdoptedDeployment | null;

          /**
           * Whether a clear-text connection is permitted, where configuration said, and `null` where it did not.
           *
           * It is answered separately from the address because the two are configured separately: a deployment may
           * state the permission for an address the person still types. Where it is stated, the screen shows it and
           * does not offer it as a choice — the decision has already been taken by whoever installed the client.
           */
          readonly clearTextPermitted: boolean | null;
      };

// Where the chosen address is written. An address is not a credential and is stored as what it is; what the desktop
// head does with anything secret is its own question.
//
// Reached as `window.localStorage` rather than as the bare global for the reason `device/deviceStore.ts` gives: Node
// publishes a `localStorage` global of its own that wins over the document's under the test runner, so the bare name
// is two different objects behind one identifier.
const storageKey = 'mailfathom.deployment';

/**
 * The deployment this run belongs to, read against what a deployment configured.
 *
 * @param configured What the shell said the three configuration sources stated, already folded by their precedence.
 * @returns What this run is pointed at, or the refusal naming what a deployment configured wrongly.
 */
export function adoptedDeployment(configured: ConfiguredConnection): ClientDeployment {
    const permission = permittedClearText(configured.permitClearText);

    if (permission === 'notABoolean') {
        return { outcome: 'refused', refusal: 'permissionNotABoolean' };
    }

    if (configured.serviceAddress === null) {
        return { outcome: 'resolved', adopted: adoptedWithoutConfiguration(), clearTextPermitted: permission };
    }

    // A permission granted for an address written `https://` is a contradiction rather than something to correct
    // silently: one of the two is a mistake, and this client cannot know which. Correcting it either way would mean
    // deciding on somebody's behalf whether their password crosses a network in the clear.
    if (permission === true && configured.serviceAddress.toLowerCase().startsWith('https://')) {
        return { outcome: 'refused', refusal: 'clearTextContradictsAddress' };
    }

    const resolved = resolveDeploymentEntry(configured.serviceAddress, permission === true);

    if (resolved.outcome === 'refused') {
        return {
            outcome: 'refused',
            refusal: resolved.refusal === 'clearTextRefused' ? 'addressNeedsClearTextPermission' : 'addressMalformed',
        };
    }

    return {
        outcome: 'resolved',
        adopted: { deployment: resolved.deployment, origin: 'configured' },
        clearTextPermitted: permission,
    };
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

/**
 * What a permission written as text says, or that it says nothing this client can read.
 *
 * Only the two words, in either case. A permission spelled `yes`, `1`, or `on` is refused rather than guessed at,
 * because every guess a permission parser makes is a guess about whether a password travels encrypted, and the one
 * mistake nobody may make is reading an operator's `no` as consent.
 */
function permittedClearText(stated: string | null): boolean | null | 'notABoolean' {
    if (stated === null) {
        return null;
    }

    const written = stated.toLowerCase();

    if (written === 'true') {
        return true;
    }

    return written === 'false' ? false : 'notABoolean';
}

/** The deployment this run belongs to where configuration named none: the one somebody chose, else the one serving. */
function adoptedWithoutConfiguration(): AdoptedDeployment | null {
    const chosen = chosenDeployment();
    if (chosen !== null) {
        return { deployment: chosen, origin: 'chosen' };
    }

    const serving = servingDeployment();

    return serving === null ? null : { deployment: serving, origin: 'serving' };
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
