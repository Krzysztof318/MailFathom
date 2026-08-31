// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/** The route prefix the client surface is served beneath, which is the deployment's to host and not the client's to choose. */
export const clientRoutePrefix = '/api/client';

/**
 * Where a MailFathom deployment is, as the address every route on it is appended to.
 *
 * It is a scheme, a host, and a port where the deployment uses one, with no trailing separator. Nothing in this
 * package composes one from a literal: a deployment is somewhere only its owner knows, so an address arrives from
 * `resolveDeploymentEntry`, which is also where the rule refusing a clear-text one lives.
 */
export interface DeploymentAddress {
    readonly baseAddress: string;
}

/**
 * Who is asking, and where.
 *
 * The credential is held as the finished header value rather than as a user name and a password, so nothing in this
 * package composes one and nothing in it can log the parts. What builds that value belongs to whatever signed the
 * person in.
 *
 * It carries the address rather than referring to one, which is what makes a credential unable to outlive the
 * deployment it was presented to: a session is built from an address, so changing the address builds a new session or
 * none, and there is nowhere for the old one to survive.
 */
export interface ClientSession extends DeploymentAddress {
    readonly authorization: string;
}

/** The absolute path a route on the client surface is reached at, for a deployment serving it from a base address. */
export function routeFor(deployment: DeploymentAddress, route: string): string {
    return `${deployment.baseAddress}${clientRoutePrefix}${route}`;
}

/** The headers every request on this surface carries. */
export function headersFor(session: ClientSession): Readonly<Record<string, string>> {
    return { Accept: 'application/json', Authorization: session.authorization };
}
