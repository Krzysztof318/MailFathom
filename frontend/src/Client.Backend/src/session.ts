// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { context, propagation } from '@opentelemetry/api';

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

/**
 * The headers every request on this surface carries.
 *
 * Beside the two it composes, it carries the W3C trace context of whatever span is open around the call, which is what
 * makes the span the deployment opens for this request a child of the client's rather than the root of a trace that
 * says nothing about who asked. `spanned` is what opens that span and what makes it the active context; where nothing
 * did, the registered propagator writes no header and the request begins a trace at the deployment as before.
 *
 * The context is read here rather than at each call site because this is the one place a request on this surface
 * composes its headers. What that requires of a call site is that it compose the request before it awaits anything:
 * the client registers a stack-based context manager, which holds the active context across the synchronous run of an
 * operation and not across a suspension, so an operation that awaited first would carry no context rather than the
 * wrong one. Every operation in this package composes its request first.
 *
 * Only the trace identifier travels. Nothing here sets baggage, so the propagator writes none — and the client surface
 * admits `traceparent` alone, so a value rather than an identifier would arrive as a refused preflight.
 */
export function headersFor(session: ClientSession): Readonly<Record<string, string>> {
    const headers: Record<string, string> = { Accept: 'application/json', Authorization: session.authorization };

    propagation.inject(context.active(), headers);

    return headers;
}
