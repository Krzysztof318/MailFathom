// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/** The route prefix the client surface is served beneath, which is the deployment's to host and not the client's to choose. */
export const clientRoutePrefix = '/api/client';

/**
 * Who is asking, and where.
 *
 * The credential is held as the finished header value rather than as a user name and a password, so nothing in this
 * package composes one and nothing in it can log the parts. What builds that value belongs to whatever signed the
 * person in.
 */
export interface ClientSession {
    readonly baseAddress: string;
    readonly authorization: string;
}

/** The absolute path a route on the client surface is reached at, for a session serving it from a base address. */
export function routeFor(session: ClientSession, route: string): string {
    return `${session.baseAddress}${clientRoutePrefix}${route}`;
}

/** The headers every request on this surface carries. */
export function headersFor(session: ClientSession): Readonly<Record<string, string>> {
    return { Accept: 'application/json', Authorization: session.authorization };
}
