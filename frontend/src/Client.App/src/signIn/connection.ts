// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { resolveDeploymentEntry } from '@mailfathom/client-backend';

// What the address somebody typed actually resolves to, before they hand it a password: which protocol this will be,
// which host and port it will reach, and whether anything is encrypted. The sign-in screen says it in the port hint
// under the field and in the disclosure beside it, which is why it sits here rather than inside either.
//
// Every value is read back out of what `Client.Backend` resolved rather than parsed a second time. That is the point:
// the rule saying which addresses may carry a credential is the wire's, and a screen that re-derived it would
// eventually disagree with the thing that actually makes the request — telling somebody their password is going over
// TLS while it goes in the clear.

/** The port a scheme reaches when the address named none. */
const schemePorts: Readonly<Record<'http' | 'https', string>> = { http: '80', https: '443' };

/** What the client will reach, read back off the address `Client.Backend` resolved. */
export interface ResolvedConnection {
    readonly secure: boolean;
    readonly authority: string;

    /** The port the address named, or `null` where the scheme's own is what will be reached. */
    readonly port: string | null;
}

/**
 * What the typed entry resolves to, or `null` where it resolves to no address at all.
 *
 * The split is on the last colon rather than the first, because an IPv6 authority is written with colons inside
 * brackets and only a port can follow the closing one.
 */
export function resolveConnection(entry: string, clearTextPermitted: boolean): ResolvedConnection | null {
    const resolved = resolveDeploymentEntry(entry, clearTextPermitted);

    if (resolved.outcome === 'refused') {
        return null;
    }

    const secure = resolved.deployment.baseAddress.startsWith('https://');
    const authority = resolved.deployment.baseAddress.replace(/^https?:\/\//u, '');
    const named = /:(\d{1,5})$/u.exec(authority);

    return { secure, authority, port: named?.[1] ?? null };
}

/** The port that will be reached, whether the address named it or the scheme supplies it. */
export function portOf(connection: ResolvedConnection): string {
    return connection.port ?? defaultPortOf(connection);
}

/**
 * The port this connection's scheme reaches on its own, whatever the address named.
 *
 * It is what the hint under the field promises — *without one this client reaches …* — which is a sentence about the
 * port that would be reached in the absence of the one that is there, so it cannot be answered by `portOf`: reading
 * that back would tell somebody who typed `:8443` that dropping it reaches `8443`.
 */
export function defaultPortOf(connection: ResolvedConnection): string {
    return schemePorts[connection.secure ? 'https' : 'http'];
}

/**
 * The port the hint names before anything has been typed, which is the scheme's own.
 *
 * It reads the permission rather than an address for the reason `portOf` reads a resolved connection: the scheme is
 * decided by whether clear text was permitted, and a screen that spelled the number itself would be a second copy of
 * the table above.
 */
export function portForPermission(clearTextPermitted: boolean): string {
    return schemePorts[clearTextPermitted ? 'http' : 'https'];
}
