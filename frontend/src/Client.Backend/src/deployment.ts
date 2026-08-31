// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, read, type ClientResult } from './failure';
import { routeFor, type DeploymentAddress } from './session';
import { send, type MailFathomTransport } from './transport';

// Where the client's address is decided, and it is here rather than in the screen that collects one because an address
// is a security boundary rather than a convenience: every request carries the credential on it, so the rule saying
// which addresses may carry one belongs beside the wire it travels on. Nothing here holds a default, and nothing here
// composes an address from a literal — a deployment is somewhere only its owner knows.

/** The route a deployment reports itself at, relative to the client prefix. It needs no grant, which is what lets a client reach it before it holds a credential. */
export const deploymentSessionRoute = '/session';

/** The name every MailFathom surface challenges under, which is what a refusal proves the client reached one by. */
const protectionSpace = 'realm="MailFathom"';

/** The product name the session route answers with. */
const productName = 'MailFathom';

/** Why an address a person gave was not taken. */
export type DeploymentEntryRefusal = 'blank' | 'malformed' | 'clearTextRefused';

/** What an address a person gave resolved to, or why it did not. */
export type DeploymentEntryResult =
    | { readonly outcome: 'resolved'; readonly deployment: DeploymentAddress }
    | { readonly outcome: 'refused'; readonly refusal: DeploymentEntryRefusal };

/** What a deployment says about itself to a caller holding no credential yet. */
export interface DeploymentGreeting {
    /** The release it reports, or `null` where it answered a challenge rather than a body. */
    readonly version: string | null;
}

/**
 * Turns what somebody typed into the address of a deployment, or refuses it.
 *
 * A person names their deployment the way they would name a server — a host, and a port where their deployment uses
 * one — so the scheme is the client's to supply, and it supplies `https`. Plain HTTP is not a fallback the client
 * takes when TLS fails: it is reached only by declaring it, because a Basic password travels on every request and
 * encoding is not encryption. The one address that needs no declaration is one on this machine, where there is no
 * network between the client and the deployment for anybody to read the credential off.
 *
 * @param entry What was typed, which may carry a scheme and may be surrounded by whitespace.
 * @param clearTextPermitted Whether the person declared that plain HTTP is acceptable for this address.
 * @returns The address, or the refusal naming why there is none.
 */
export function resolveDeploymentEntry(entry: string, clearTextPermitted: boolean): DeploymentEntryResult {
    const typed = entry.trim();
    if (typed.length === 0) {
        return { outcome: 'refused', refusal: 'blank' };
    }

    const address = typed.length > longestEntry ? null : parseAddress(typed, clearTextPermitted ? 'http' : 'https');

    if (address === null) {
        return { outcome: 'refused', refusal: 'malformed' };
    }

    if (address.scheme === 'http' && !clearTextPermitted && !isThisMachine(address.host)) {
        return { outcome: 'refused', refusal: 'clearTextRefused' };
    }

    return { outcome: 'resolved', deployment: { baseAddress: `${address.scheme}://${address.authority}` } };
}

/**
 * Asks an address whether MailFathom is what answers there, before anything is sent to it.
 *
 * It is one request at the scheme the address names and there is no second one: a deployment that refused a TLS
 * connection reports that refusal rather than being tried again without it. The route it reaches needs no grant, so
 * this answers for a client that has nothing to sign in with yet — and a deployment that demands a credential proves
 * itself by the protection space its refusal challenges under.
 *
 * @param deployment The address to reach.
 * @param transport How the request goes out.
 * @returns What the deployment reported, or why the address was not taken as one.
 */
export async function reachDeployment(
    deployment: DeploymentAddress,
    transport: MailFathomTransport,
): Promise<ClientResult<DeploymentGreeting>> {
    const response = await send(transport, {
        method: 'GET',
        path: routeFor(deployment, deploymentSessionRoute),
        headers: { Accept: 'application/json' },
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 200) {
        const version = versionReported(response.body);

        return version === null ? failed('unreadable', response.status) : read({ version });
    }

    // A deployment serving this surface refuses a caller carrying no credential, which is the answer a client asking
    // where it is gets from every deployment somebody actually runs. The challenge is what makes that answer say more
    // than "something refused me": every MailFathom surface names one protection space, and a browser can read the
    // header because the client endpoint's own CORS policy exposes it.
    if (response.status === 401 && challengesAsMailFathom(response.headers)) {
        return read({ version: null });
    }

    return failed(response.status >= 500 ? 'unavailable' : 'unreadable', response.status);
}

/** The release a session body reports, or `null` where the body was not one MailFathom answers with. */
function versionReported(body: string): string | null {
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
    const service = answered['service'];
    const version = answered['version'];

    if (service !== productName || typeof version !== 'string') {
        return null;
    }

    return version.length > 0 && version.length <= longestVersion ? version : null;
}

function challengesAsMailFathom(headers: Readonly<Record<string, string>>): boolean {
    return headers['www-authenticate']?.includes(protectionSpace) === true;
}

/** Where a deployment is, once what was typed has been read as a scheme, a host, and a port. */
interface ParsedAddress {
    readonly scheme: 'http' | 'https';

    /** The host alone, which is what the clear-text rule is decided against. */
    readonly host: string;

    /** The host with the port where one is not the scheme's own, which is what an address is written as. */
    readonly authority: string;
}

// A scheme is one the person wrote rather than one this function guessed, so it is recognized only in the form that
// cannot be confused with a host and a port: `mail.example.test:8443` names a port, and `https://mail.example.test`
// names a scheme.
const writtenScheme = /^([a-z][a-z0-9+.-]*):\/\/(.*)$/i;

// A host is a name or an IPv4 address, written as labels a resolver would accept. An IPv6 address is the bracketed
// form beside it, which is why the port is split off before either is matched.
//
// Each character has exactly one place to go in this pattern — a separator is only ever consumed by the group that
// asks for it, so nothing here has a second way to match and there is no backtracking to walk. Written the more
// obvious way, as an optional trailing group inside each label, a host of ten ten-character labels that ends in one
// character too many takes billions of attempts to refuse: this input arrives from a person typing into a screen, and
// a form that stops responding to a typo is a defect rather than a slow path.
const hostName = /^[a-z0-9](-*[a-z0-9])*(\.[a-z0-9](-*[a-z0-9])*)*$/i;
const bracketedHost = /^\[[0-9a-f:.]+\]$/i;

// The longest entry read at all. A host name is at most 253 characters and a scheme and a port add a few more, so
// anything past this is not an address somebody mistyped — and a bound is what keeps the work below proportional to
// what a person can mean rather than to what can be pasted.
const longestEntry = 320;

// The longest release string read out of a session answer. It is a version rather than prose, and a bound here is what
// keeps an answer from an address nobody has trusted yet from becoming an unbounded string the client carries around.
const longestVersion = 64;

const defaultPorts: Readonly<Record<'http' | 'https', string>> = { http: '80', https: '443' };

/**
 * Reads what was typed as the address of a deployment, or answers `null` where it is not one.
 *
 * Written out rather than handed to the platform's own parser because this package declares no DOM and no Node
 * library, which is what keeps `Client.Backend` free of a runtime — and because the two parsers a runtime offers are
 * lenient in exactly the places that matter here: an address carrying a path, a query, or a credential is one somebody
 * pasted from a browser rather than one they named, and each has to be refused rather than quietly dropped.
 */
function parseAddress(typed: string, suppliedScheme: 'http' | 'https'): ParsedAddress | null {
    const written = writtenScheme.exec(typed);
    const scheme = written === null ? suppliedScheme : written[1]?.toLowerCase();
    const authority = written === null ? typed : (written[2] ?? '');

    if (scheme !== 'http' && scheme !== 'https') {
        return null;
    }

    const separated = separatePort(authority);
    if (separated === null) {
        return null;
    }

    const { host, port } = separated;

    if (!hostName.test(host) && !bracketedHost.test(host)) {
        return null;
    }

    const named = host.toLowerCase();
    const carried = port === null || port === defaultPorts[scheme] ? named : `${named}:${port}`;

    return { scheme, host: named, authority: carried };
}

/** The host and the port an authority names, or `null` where it names something that is not an authority. */
function separatePort(authority: string): { host: string; port: string | null } | null {
    // Everything that would make this more than a host and a port: a path, a query, a fragment, a credential, or
    // whitespace somebody typed in the middle of it.
    if (/[/?#@\s]/.test(authority)) {
        return null;
    }

    const bracketed = /^(\[[^\]]*\])(?::(\d{1,5}))?$/.exec(authority) ?? /^([^:]*)(?::(\d{1,5}))?$/.exec(authority);
    if (bracketed === null) {
        return null;
    }

    const host = bracketed[1] ?? '';
    const port = bracketed[2] ?? null;

    if (host.length === 0) {
        return null;
    }

    return port === null || (Number(port) >= 1 && Number(port) <= 65535) ? { host, port } : null;
}

/**
 * Whether the host is this machine, which is the one place clear text crosses no network.
 *
 * Only the loopback host itself counts. A name that merely ends in `.localhost` is a name somebody chose, resolved by
 * whatever resolver the machine is configured with, and treating it as loopback would hand the exemption to any host
 * that asked for it by name.
 */
function isThisMachine(hostname: string): boolean {
    const host = hostname.startsWith('[') && hostname.endsWith(']') ? hostname.slice(1, -1) : hostname;

    return host === 'localhost' || host === '::1' || /^127(\.\d{1,3}){3}$/.test(host);
}
