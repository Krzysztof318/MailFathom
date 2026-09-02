// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { headersFor, routeFor, type ClientSession } from './session';
import { send, type MailFathomTransport } from './transport';

/** The route a deployment reports itself and the caller's grant at, relative to the client prefix. */
export const deploymentSessionRoute = '/session';

/** The product name the session route answers with, which is what proves an address is a deployment rather than whatever else answers a port. */
const productName = 'MailFathom';

/**
 * The permissions a credential presented here can carry.
 *
 * It is the mail half of the published set and the whole of it, because that is the half this surface draws on: an
 * administrative name is refused on this endpoint rather than reported as a grant nobody could use. The client acts on
 * two of them today and names all eight anyway — this is the contract the deployment publishes rather than a list of
 * what one screen happens to read, so a screen arriving later finds the name already here.
 */
export const mailPermissions = [
    'mailfathom.mail.read',
    'mailfathom.mail.ask',
    'mailfathom.mail.flags.write',
    'mailfathom.mail.drafts.write',
    'mailfathom.mail.send',
    'mailfathom.mail.accounts.write',
    'mailfathom.mail.contacts.read',
    'mailfathom.mail.contacts.write',
] as const;

/** One name out of the half of the published set this surface draws on. */
export type MailFathomPermission = (typeof mailPermissions)[number];

/** What a deployment answers about itself and about the credential that just reached it. */
export interface DeploymentSession {
    /** The release the deployment is running, which is what says which contract the client is talking to. */
    readonly version: string;

    /**
     * What the presented credential is allowed to do here, and nothing about anybody else's.
     *
     * A grant carrying nothing is an empty list rather than a refusal, because that is the accurate answer for a
     * credential narrowed to nothing — and it is what lets the client say so instead of offering an action that would
     * be turned away.
     */
    readonly permissions: readonly MailFathomPermission[];
}

// The most names one answer may carry. The published set is smaller than this by a wide margin and may grow; what the
// bound is for is the answer that is not a grant at all, which is refused before the array is walked rather than after.
const mostPermissionsInAnswer = 64;

// The longest session answer read at all. This is the one answer in the client that arrives from an address nobody has
// trusted yet — the whole point of asking is that the client does not know what is there — so the bound is applied
// before the body is expanded rather than to what parsing it produced. It names a product, a release, and the caller's
// own grant out of a published set, which is a few hundred bytes; anything past this is not a session answer, whatever
// it turns out to be. `longestResponseBody` is the transport's backstop behind it, and this is the bound that says what
// *this* route may answer with.
const longestSessionBody = 4096;

/**
 * Reads what the deployment says about itself and about the credential presented to it.
 *
 * It is what the client asks first on every start it is already signed in for: the grant decides what the application
 * may offer, and a credential a deployment has stopped accepting is found here rather than by an action failing.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @returns The deployment's version and this caller's grant, or why the answer never arrived.
 */
export async function readDeploymentSession(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<DeploymentSession>> {
    const response = await send(transport, {
        method: 'GET',
        path: routeFor(session, deploymentSessionRoute),
        headers: headersFor(session),
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const answered = parseDeploymentSession(response.body);

    return answered === null ? failed('unreadable', response.status) : read(answered);
}

/**
 * What a session answer says, or `null` where the body is not one MailFathom writes.
 *
 * A name this client does not know is dropped rather than refused, which is the one deliberate leniency here: the
 * published set grows, and a client that refused the whole answer over a name added after it was built would stop
 * working against a deployment newer than itself. Every other departure from the shape is a refusal, because this body
 * arrives from an address nobody has established anything about.
 */
export function parseDeploymentSession(body: string): DeploymentSession | null {
    if (body.length > longestSessionBody) {
        return null;
    }

    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        return null;
    }

    const answered = parsed as Record<string, unknown>;
    const version = answered['version'];
    const granted = answered['permissions'];

    if (answered['service'] !== productName || typeof version !== 'string' || version.length === 0) {
        return null;
    }

    if (!Array.isArray(granted) || granted.length > mostPermissionsInAnswer) {
        return null;
    }

    const permissions: MailFathomPermission[] = [];
    for (const name of granted) {
        if (typeof name !== 'string') {
            return null;
        }

        if (isMailPermission(name) && !permissions.includes(name)) {
            permissions.push(name);
        }
    }

    return { version, permissions };
}

function isMailPermission(value: string): value is MailFathomPermission {
    return (mailPermissions as readonly string[]).includes(value);
}
