// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { deploymentSessionRoute, parseDeploymentSession } from './deploymentSession';
import { failed, read, type ClientResult } from './failure';
import { headersFor, routeFor, type ClientSession, type DeploymentAddress } from './session';
import { send, type MailFathomTransport } from './transport';

// Signing in against a deployment, in two steps and deliberately not one. The route that reports what a caller may do
// answers a caller carrying nothing as well as one carrying a credential, and asking it twice is what keeps a password
// from being handed to whatever is at an address somebody mistyped: the first request establishes that MailFathom is
// there and that it takes passwords at all, and only then does the second present one.
//
// It buys nothing against an address somebody typed deliberately — anything can answer as MailFathom — and it is not
// there for that. What it is there for is the ordinary typo, which lands on a real host that would otherwise be sent a
// password composed for somewhere else, and which the client cannot take back once it has.

/** The name every MailFathom surface challenges under, which is what a refusal proves the client reached one by. */
const protectionSpace = 'realm="MailFathom"';

// The scheme named as a scheme rather than as a word, so a parameter whose value happens to read as one is not mistaken
// for a challenge. A surface accepting passwords answers with two challenges in one header — the bare bearer one every
// method produces, and the password one beside it — so what decides this is whether `Basic` leads one of them.
const offersBasicScheme = /(^|,)\s*basic(\s|,|$)/;

/** Why a deployment did not sign a credential in. Each is an answer the deployment gave rather than a failure to reach it. */
export type SignInRefusal = 'credentialRefused' | 'basicNotOffered';

/** What a deployment said about itself to a caller carrying nothing. */
export interface DeploymentGreeting {
    /**
     * Whether this client may present a user name and a password here.
     *
     * A deployment that challenges without naming `Basic` accepts some other method, and a password sent to it would be
     * refused whatever it was. A deployment that answers a caller carrying nothing requires no credential at all, which
     * this reports as `true` rather than as a third case: it will take a request that carries one, and asking somebody
     * to notice the difference would be asking them about a configuration that is not theirs.
     */
    readonly acceptsPassword: boolean;
}

/**
 * Asks a deployment what it is, carrying no credential.
 *
 * This is the request that decides whether a password may be sent to an address at all, so it sends none: what comes
 * back is either MailFathom's own answer or a reason there is nothing here to sign in to.
 *
 * @param deployment The address to ask, which nobody has established is a deployment yet.
 * @param transport How the request goes out.
 * @returns What the deployment said about itself, or why there was no answer to read.
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
        return answersAsMailFathom(response.body)
            ? read({ acceptsPassword: true })
            : failed('unreadable', response.status);
    }

    if (response.status === 401 && challengesAsMailFathom(response.headers)) {
        return read({ acceptsPassword: offersBasic(response.headers) });
    }

    return failed(response.status >= 500 ? 'unavailable' : 'unreadable', response.status);
}

/**
 * What a deployment made of the credential presented to it.
 *
 * A refusal is a value rather than a failure because the deployment answered: it read the credential and would not take
 * it, or it does not take passwords at all. What never reached an answer is a `ClientFailure` around this.
 */
export type SignInOutcome = { readonly signedIn: true } | { readonly signedIn: false; readonly refusal: SignInRefusal };

/**
 * Presents a credential to a deployment and reports what it decided.
 *
 * There is one request and no retry: a deployment that refused a TLS connection, answered as something other than
 * MailFathom, or turned the credential away is reported as it answered rather than tried again another way. Where the
 * address was typed rather than handed down, `reachDeployment` is what runs before this.
 *
 * @param session The address to reach and the finished header value to present, which this package composes no part of.
 * @param transport How the request goes out.
 * @returns Whether the deployment signed the credential in, or why the answer never arrived.
 */
export async function signIn(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<SignInOutcome>> {
    const response = await send(transport, {
        method: 'GET',
        path: routeFor(session, deploymentSessionRoute),
        headers: headersFor(session),
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 200) {
        return answersAsMailFathom(response.body) ? read({ signedIn: true }) : failed('unreadable', response.status);
    }

    // A refusal is read only where the challenge proves MailFathom produced it, for the reason the challenge is read at
    // all: this answer arrives from an address the person typed, and something else refusing a request is a wrong
    // address rather than a wrong password. Which of the two refusals it is then follows from the schemes offered — a
    // deployment that has not enabled passwords challenges without naming one, and telling somebody their password was
    // rejected there would send them to change a password that was never read.
    if (response.status === 401 && challengesAsMailFathom(response.headers)) {
        return read(
            offersBasic(response.headers)
                ? { signedIn: false, refusal: 'credentialRefused' }
                : { signedIn: false, refusal: 'basicNotOffered' },
        );
    }

    // A grant this credential does not hold is the one refusal that is about what an owner may do rather than about who
    // they are, so it is never a reason to ask for the password again. Nothing else the deployment can answer here says
    // anything about the credential: a failing deployment is retried, and anything else is not MailFathom answering.
    if (response.status === 403) {
        return failed('unauthorized', response.status);
    }

    return failed(response.status >= 500 ? 'unavailable' : 'unreadable', response.status);
}

/**
 * Whether the session answer is one MailFathom writes, which is what proves the address is a deployment.
 *
 * It reads the same answer `readDeploymentSession` reads and through the same parser, because the two questions are one
 * question asked at different moments: an address that answers as a session is a deployment, and a second reading of
 * the same body would be a second thing to keep in step with the route.
 */
function answersAsMailFathom(body: string): boolean {
    return parseDeploymentSession(body) !== null;
}

function challengesAsMailFathom(headers: Readonly<Record<string, string>>): boolean {
    return headers['www-authenticate']?.includes(protectionSpace) === true;
}

function offersBasic(headers: Readonly<Record<string, string>>): boolean {
    return offersBasicScheme.test(headers['www-authenticate']?.toLowerCase() ?? '');
}
