// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { clientRoutePrefix, routeFor, type DeploymentAddress } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What a deployment offers somebody signing in, read before anything is drawn. It is the one question the two documents
// a client could already read do not answer between them: the session route's challenge says whether a password would
// be taken, and the RFC 9728 document names the issuers a token may come from, but neither says what a *screen* needs —
// which servers a person may be offered a control for, what to write on it, and the client identifier the authorization
// request has to start with.
//
// Both documents are read here because both are read before any credential exists, and neither is worth anything to a
// screen on its own: the sign-in methods say which servers are offered, and the protected resource document says what a
// token must be issued for and which scopes to ask for. A deployment that publishes neither is an older one, and a
// client that reads nothing from it still signs somebody in with a password — `reachDeployment` is unchanged and is
// what says so.

/** The route the sign-in methods answer on, relative to the client prefix. */
export const signInMethodsRoute = '/sign-in-methods';

/** Where the RFC 9728 document for the client surface is published, which the specification places outside the prefix. */
export const protectedResourceMetadataRoute = `/.well-known/oauth-protected-resource${clientRoutePrefix}`;

/**
 * The most of either document this package reads before refusing it, in bytes.
 *
 * Both are read from an address nobody has trusted yet, so the bound is the point rather than a formality. A deployment
 * publishing a dozen authorization servers, each with an issuer, two names, and a client identifier, sits far inside
 * it, and so does a protected resource document listing the whole permission vocabulary as scopes.
 */
export const longestSignInMethodsAnswer = 16_384;

/** The most servers one deployment may offer, past which the answer is not one a person was meant to choose from. */
export const mostAuthorizationServers = 24;

/** The most scopes one resource may ask for, bounded for the reason every collection this package walks is. */
const mostScopes = 64;

/** The longest any single published name, identifier, or address may be. */
const longestPublishedValue = 512;

/** One authorization server a person may sign in to this deployment through. */
export interface SignInAuthorizationServer {
    /** The issuer identifier, which the server's own discovery document is found from and which a token must name. */
    readonly issuer: string;

    /**
     * What the deployment calls this server.
     *
     * `self` means the deployment's own identity provider, and says only that the control is the primary way in rather
     * than a third party's. Any other name is the deployment's name for somebody else's server, which a client matches
     * against the provider marks it ships.
     */
    readonly name: string;

    /** The words the control carries, which the deployment defaults to {@link name}. */
    readonly displayName: string;

    /** The identifier the authorization request is started with, registered by the operator at that server. */
    readonly clientId: string;
}

/** The name a deployment's own identity provider is published under, which is a drawing decision and not a role. */
export const ownProviderName = 'self';

/** What a deployment offers somebody signing in, which is the whole of what a sign-in screen is composed from. */
export interface SignInMethods {
    /** Whether a user name and a password may be presented here. */
    readonly acceptsPassword: boolean;

    /** The servers a person may sign in through, in the order the deployment published them. */
    readonly authorizationServers: readonly SignInAuthorizationServer[];
}

/**
 * Asks a deployment what a sign-in screen may offer, carrying no credential.
 *
 * @param deployment The address to ask.
 * @param transport How the request goes out.
 * @returns What the deployment offers, or why there was nothing to read.
 *
 * A deployment that does not publish this route answers `missing`, which is the one status this route reads as naming a
 * single thing rather than as an unreachable deployment: it is an older deployment rather than a broken one, and the
 * caller falls back to what the session route's challenge already says about a password.
 */
export function readSignInMethods(
    deployment: DeploymentAddress,
    transport: MailFathomTransport,
): Promise<ClientResult<SignInMethods>> {
    return spanned(`GET ${signInMethodsRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(deployment, signInMethodsRoute),
            headers: { Accept: 'application/json' },
            longestAnswer: longestSignInMethodsAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const methods = parseSignInMethods(response.body);

        return methods === null ? failed('unreadable', response.status) : read(methods);
    });
}

/** What a token presented to this deployment has to have been issued for. */
export interface ProtectedResource {
    /** The identifier the authorization request names as RFC 8707's `resource`, so the token is audienced to it. */
    readonly resource: string;

    /** The scopes a client should ask for, which is what RFC 9728 defines the field as rather than what is enforced. */
    readonly scopes: readonly string[];
}

/**
 * Reads what a token this deployment accepts has to have been issued for, carrying no credential.
 *
 * @param deployment The address to ask.
 * @param transport How the request goes out.
 * @returns The resource and the scopes, or why there was nothing to read.
 */
export function readProtectedResource(
    deployment: DeploymentAddress,
    transport: MailFathomTransport,
): Promise<ClientResult<ProtectedResource>> {
    return spanned(`GET ${protectedResourceMetadataRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${deployment.baseAddress}${protectedResourceMetadataRoute}`,
            headers: { Accept: 'application/json' },
            longestAnswer: longestSignInMethodsAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const resource = parseProtectedResource(response.body, deployment);

        return resource === null ? failed('unreadable', response.status) : read(resource);
    });
}

function parseSignInMethods(body: string): SignInMethods | null {
    const document = readDocument(body);

    if (document === null || typeof document['acceptsPassword'] !== 'boolean') {
        return null;
    }

    const published = document['authorizationServers'];

    // A deployment that names no servers at all is one that publishes none, on the same reading RFC 9728's optional
    // scope list is given: an absent field is an answer rather than a document this package refuses.
    if (published === undefined) {
        return { acceptsPassword: document['acceptsPassword'], authorizationServers: [] };
    }

    if (!Array.isArray(published) || published.length > mostAuthorizationServers) {
        return null;
    }

    const servers: SignInAuthorizationServer[] = [];

    for (const entry of published) {
        const server = parseAuthorizationServer(entry);

        if (server === null) {
            return null;
        }

        servers.push(server);
    }

    return { acceptsPassword: document['acceptsPassword'], authorizationServers: servers };
}

function parseAuthorizationServer(entry: unknown): SignInAuthorizationServer | null {
    const published = asRecord(entry);

    if (published === null) {
        return null;
    }

    const issuer = publishedValue(published['issuer']);
    const name = publishedValue(published['name']);
    const displayName = publishedValue(published['displayName']);
    const clientId = publishedValue(published['clientId']);

    if (issuer === null || name === null || displayName === null || clientId === null) {
        return null;
    }

    // An issuer is the one published field this client turns into an address it will call, so it is checked for being
    // one rather than for being a string. Everything else is a name a screen draws.
    return isSecureAddress(issuer) ? { issuer, name, displayName, clientId } : null;
}

function parseProtectedResource(body: string, deployment: DeploymentAddress): ProtectedResource | null {
    const document = readDocument(body);

    if (document === null) {
        return null;
    }

    const resource = publishedValue(document['resource']);
    const published = document['scopes_supported'];

    // The document has to name the address it was read from, which RFC 9728 requires of a client and which this is the
    // whole of the enforcement of. What the field becomes is the `resource` on every authorization and token request
    // this client makes, so a deployment naming somebody else's address would have an honest authorization server
    // issue a token audienced to *them* — which that deployment could then replay there. It is the same check
    // `oauthSignIn.ts` makes on a discovery document's `issuer`, against the other document read from an origin
    // nobody has trusted yet.
    if (resource === null || !namesTheDeployment(resource, deployment)) {
        return null;
    }

    // The field is optional in RFC 9728, and a resource that advertises none is a client asking for none rather than a
    // document this package refuses.
    if (published === undefined) {
        return { resource, scopes: [] };
    }

    if (!Array.isArray(published) || published.length > mostScopes) {
        return null;
    }

    const scopes: string[] = [];

    for (const entry of published) {
        const scope = publishedValue(entry);

        if (scope === null) {
            return null;
        }

        scopes.push(scope);
    }

    return { resource, scopes };
}

/**
 * Whether a published resource identifier is the one this deployment's own document may name.
 *
 * The address a client reaches is the scheme and the authority, and the endpoint's routes answer under one prefix — so
 * the identifier a token is issued for is those two written together, and the deployment refuses to start where it is
 * configured as anything else. Compared without case because a host is case-insensitive and the prefix is a literal.
 */
function namesTheDeployment(resource: string, deployment: DeploymentAddress): boolean {
    return resource.toLowerCase() === `${deployment.baseAddress}${clientRoutePrefix}`.toLowerCase();
}

function readDocument(body: string): Readonly<Record<string, unknown>> | null {
    try {
        return asRecord(JSON.parse(body));
    } catch {
        return null;
    }
}

/** A published string, trimmed of nothing and refused where it is absent, empty, or longer than one could be. */
function publishedValue(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestPublishedValue ? value : null;
}

/**
 * Whether the value is an absolute `https` address with no credential, query, or fragment in it.
 *
 * Written out rather than handed to the platform's parser for the reason `deployment.ts` writes its own: this package
 * declares no DOM and no Node library, so there is no `URL` here. What it has to refuse is what an address carrying one
 * of those would do — an issuer with user information in it is a credential this client would put in a request line,
 * and one with a query is not an identifier a discovery document can be derived from.
 */
export function isSecureAddress(value: string): boolean {
    return /^https:\/\/[^\s/?#@]+(\/[^\s?#]*)?$/u.test(value);
}

/**
 * Whether the value is an absolute `https` endpoint with no credential and no fragment, and a query it may keep.
 *
 * The difference from {@link isSecureAddress} is the query, and it is the whole difference: RFC 6749 §3.1 permits an
 * authorization endpoint to carry one, and a server that publishes one means it — while an *issuer* carrying one is not
 * an identifier a discovery document can be derived from. So an endpoint out of a document is read with this and an
 * issuer with the other.
 */
export function isSecureEndpoint(value: string): boolean {
    return /^https:\/\/[^\s/?#@]+(\/[^\s?#]*)?(\?[^\s#]*)?$/u.test(value);
}

/**
 * The scheme, host, and port an address names, which is what a content security policy and a same-server check read.
 *
 * @param address An address {@link isSecureAddress} or {@link isSecureEndpoint} has accepted.
 * @returns The origin, with no trailing separator.
 */
export function originOf(address: string): string {
    const authority = address.slice('https://'.length);
    const separated = authority.search(/[/?]/u);

    return `https://${separated < 0 ? authority : authority.slice(0, separated)}`;
}
