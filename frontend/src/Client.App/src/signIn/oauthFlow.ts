// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Signing in with an authorization server, from the moment somebody picks a provider to the moment this client holds a
// grant. `Client.Backend` composes the addresses and sends the requests, `pkce.ts` generates what one attempt is
// started with, `signInRedirect.ts` is how the person gets to the server and back, and this is the sequence that puts
// the three together.
//
// It is written as two halves — starting and completing — because on the web head they happen in two different runs of
// the client: starting replaces the document, and what comes back arrives at the next start with nothing in memory. So
// everything the second half needs is written down by the first, in the tab's own storage, and the shell head runs the
// same two halves back to back without noticing that they could have been apart.
//
// **What is written down is a secret with the life of one sign-in.** The verifier is what redeems the code, so it is
// removed the moment the answer arrives — matched or not — rather than left for a later run to find. `sessionStorage`
// and not the credential store, because this is not something kept: it belongs to the tab that started the attempt, and
// a second tab finding it would be a second tab able to redeem somebody else's code.

import {
    authorizationRequestAddress,
    readAuthorizationServer,
    readOwnDisplayName,
    redeemAuthorizationCode,
    type DeploymentAddress,
    type MailFathomTransport,
    type ProtectedResource,
    type SignInAuthorizationServer,
} from '@mailfathom/client-backend';
import { beginAuthorizationAttempt } from './pkce';
import { grantFromIssuedToken, type OAuthGrant } from './oauthGrant';
import type { SignInRedirect, SignInRedirectAnswer } from '../shellOperations/signInRedirect';

/** Why an OAuth sign-in produced no grant. */
export type OAuthSignInRefusal =
    /** The person, or the server, stopped the authorization before a code existed. */
    | 'notAuthorized'

    /** An answer this client did not start, or started and has already answered. Nothing is redeemed for it. */
    | 'unexpectedAnswer'

    /** The server read what was presented and refused it. */
    | 'refused'

    /** The token was issued and the deployment does not know whose it is, which is the empty `401` it answers with. */
    | 'notAUser'

    /** The authorization server or the deployment could not be reached, or said something unreadable. */
    | 'unavailable';

// A head that is replaced by the address it opens answers neither of these, because it stops running at the moment it
// hands over: `startOAuthSignIn` never settles there, and what comes back reaches `completeOAuthSignIn` at the next
// start. So the handing-off a screen draws is a state it entered before it called rather than an outcome it was given.
/** What came of an OAuth sign-in. */
export type OAuthSignInOutcome =
    | {
          readonly outcome: 'signedIn';

          /**
           * The deployment the grant was issued for, which the run that started the attempt wrote down.
           *
           * It travels with the outcome rather than being what the caller already had, because on the web head the two
           * halves are two runs of the client: the second one starts with nothing in memory, and the address it would
           * otherwise resolve for itself is whatever `adoptedDeployment.ts` answers now — the origin that served the
           * page, where somebody had pointed the client somewhere else before they were handed over.
           */
          readonly deployment: DeploymentAddress;

          readonly grant: OAuthGrant;
      }
    | { readonly outcome: 'refused'; readonly refusal: OAuthSignInRefusal };

/** Where one attempt in flight is written down, which is one entry because one tab has one sign-in in flight. */
const attemptEntry = 'mailfathom.signIn.attempt';

/** The most a written-down attempt may be before it is refused unread. */
const longestAttempt = 4096;

/** What the second half of a sign-in needs and cannot recompute, written down by the first half. */
interface WrittenAttempt {
    readonly state: string;
    readonly codeVerifier: string;

    /** The deployment the token is being obtained for, which is who is asked whose it is once one exists. */
    readonly baseAddress: string;

    readonly issuer: string;
    readonly clientId: string;
    readonly resource: string;
    readonly redirectUri: string;

    /** What the deployment called this provider, which is the name a person is shown where the deployment answers nothing about them. */
    readonly displayName: string;
}

/** Everything one sign-in is started against, which a screen has already read before it draws a control for it. */
export interface OAuthSignInStart {
    /** The deployment being signed in to, which is who is asked whose token this is once one exists. */
    readonly deployment: DeploymentAddress;

    readonly server: SignInAuthorizationServer;
    readonly resource: ProtectedResource;
    readonly redirect: SignInRedirect;
    readonly transport: MailFathomTransport;
}

/**
 * Starts an OAuth sign-in: discovers the server, writes the attempt down, and hands the person over.
 *
 * @param start The server the deployment published, what a token is asked for, how the person is handed over, and how requests go out.
 * @returns The grant where this head came back with the answer itself, the refusal, or that the document is being replaced.
 */
export async function startOAuthSignIn(start: OAuthSignInStart): Promise<OAuthSignInOutcome> {
    const server = await readAuthorizationServer(start.server.issuer, start.transport);

    if (server.outcome !== 'read') {
        return { outcome: 'refused', refusal: 'unavailable' };
    }

    const attempt = await beginAuthorizationAttempt();

    if (attempt === null) {
        // A head with no digest cannot compute the PKCE challenge, so there is no request to make. It is the same
        // outcome as a server that could not be read: the sign-in could not be started, and the screen says so rather
        // than standing behind a spinner nothing will settle.
        return { outcome: 'refused', refusal: 'unavailable' };
    }

    const written: WrittenAttempt = {
        state: attempt.state,
        codeVerifier: attempt.codeVerifier,
        baseAddress: start.deployment.baseAddress,
        issuer: start.server.issuer,
        clientId: start.server.clientId,
        resource: start.resource.resource,
        redirectUri: start.redirect.redirectUri,
        displayName: start.server.displayName,
    };

    if (!writeAttempt(written)) {
        // Storage that refuses the write is a sign-in that could not be completed rather than one to start anyway:
        // handing somebody to a server whose answer nothing can redeem would spend their password on nothing.
        return { outcome: 'refused', refusal: 'unavailable' };
    }

    const address = authorizationRequestAddress({
        metadata: server.value,
        clientId: start.server.clientId,
        redirectUri: start.redirect.redirectUri,
        resource: start.resource.resource,
        scopes: start.resource.scopes,
        state: attempt.state,
        codeChallenge: attempt.codeChallenge,

        // An OpenID provider is entitled to a nonce and some refuse a request without one; a server that is not one is
        // sent none rather than an extra parameter it never asked for.
        nonce: server.value.opensIdentity ? attempt.nonce : null,
    });

    const answer = await start.redirect.hand(address);

    // A shell that answered nothing could not open the browser, or nobody came back inside the wait it holds the
    // redirect port for. Both put the person back on the sign-in screen rather than in front of a control that never
    // stops waiting, and both end the verifier with the sign-in: `completeOAuthSignIn` is what takes it on every other
    // exit, and this is the one that never reaches it.
    if (answer === null) {
        discardWrittenAttempt();

        return { outcome: 'refused', refusal: 'unavailable' };
    }

    return completeOAuthSignIn(answer, start.transport);
}

/**
 * Completes an OAuth sign-in from what the redirect carried back, against the attempt that was written down for it.
 *
 * The written attempt is removed first and whatever happens afterwards, matched or not: a verifier left behind is a
 * secret outliving the one code it was for, and an answer that arrives twice must redeem nothing the second time.
 *
 * @param answer What the redirect carried back.
 * @param transport How the requests go out.
 * @returns The grant and the deployment it was obtained for, or why there is none.
 */
export async function completeOAuthSignIn(
    answer: SignInRedirectAnswer,
    transport: MailFathomTransport,
): Promise<OAuthSignInOutcome> {
    const attempt = takeWrittenAttempt();

    if (answer.state !== attempt?.state) {
        // Either nothing started this, or it started something else. A refusal carrying no state at all is the same
        // case rather than a lesser one: RFC 6749 requires the state back with an error, so an answer without it is an
        // answer nothing here can bind to an attempt.
        return { outcome: 'refused', refusal: 'unexpectedAnswer' };
    }

    if (answer.answered === 'refused') {
        return { outcome: 'refused', refusal: 'notAuthorized' };
    }

    const server = await readAuthorizationServer(attempt.issuer, transport);

    if (server.outcome !== 'read') {
        return { outcome: 'refused', refusal: 'unavailable' };
    }

    const issued = await redeemAuthorizationCode(
        {
            metadata: server.value,
            clientId: attempt.clientId,
            redirectUri: attempt.redirectUri,
            resource: attempt.resource,
            code: answer.code,
            codeVerifier: attempt.codeVerifier,
        },
        transport,
    );

    if (issued.outcome === 'refused') {
        return { outcome: 'refused', refusal: 'refused' };
    }

    if (issued.outcome !== 'issued') {
        return { outcome: 'refused', refusal: 'unavailable' };
    }

    const grant = grantFromIssuedToken(issued.token, {
        issuer: attempt.issuer,
        clientId: attempt.clientId,
        resource: attempt.resource,
        person: attempt.displayName,
    });

    return whoTheDeploymentSaysThisIs(grant, { baseAddress: attempt.baseAddress }, transport);
}

/**
 * Asks the deployment who the token belongs to, which is both the name to show and the check that it belongs to anybody.
 *
 * This client asks the authorization server for no identity token, so the name comes from the one party that knows what
 * the token means here. That makes the read two things at once: a deployment answering an empty `401` is saying the
 * subject maps to no user of its own, which is a sign-in that has not happened however good the token is, and it is the
 * one outcome that must not reach a screen as a working session.
 *
 * Anything else answers with the provider's own name instead. A deployment that is momentarily unreachable, or that
 * declines this one read, has not said the token is unknown — and signing somebody out over a name is a worse answer
 * than a name that is the provider's.
 */
async function whoTheDeploymentSaysThisIs(
    grant: OAuthGrant,
    deployment: DeploymentAddress,
    transport: MailFathomTransport,
): Promise<OAuthSignInOutcome> {
    const known = await readOwnDisplayName(
        { baseAddress: deployment.baseAddress, authorization: grant.authorization },
        transport,
    );

    if (known.outcome === 'read') {
        return { outcome: 'signedIn', deployment, grant: { ...grant, person: known.value.displayName } };
    }

    return known.failure.reason === 'unauthenticated'
        ? { outcome: 'refused', refusal: 'notAUser' }
        : { outcome: 'signedIn', deployment, grant };
}

/**
 * Discards the attempt written down for a hand-over nobody is waiting for any more.
 *
 * The verifier is what redeems the code, and a sign-in abandoned halfway leaves one behind that nothing will ever use:
 * the module's own rule is that it is a secret with the life of one sign-in, so giving up on the sign-in ends it too.
 */
export function discardWrittenAttempt(): void {
    try {
        window.sessionStorage.removeItem(attemptEntry);
    } catch {
        // A store that will not answer is a store holding nothing, which is the outcome this was asking for.
    }
}

/** Whether the attempt is written down, which storage a browser refuses answers `false` rather than throwing on. */
function writeAttempt(attempt: WrittenAttempt): boolean {
    try {
        window.sessionStorage.setItem(attemptEntry, JSON.stringify(attempt));

        return true;
    } catch {
        return false;
    }
}

/** The attempt that was written down, removed as it is read, or `null` where there is none this client wrote. */
function takeWrittenAttempt(): WrittenAttempt | null {
    let stored: string | null;

    try {
        stored = window.sessionStorage.getItem(attemptEntry);
        window.sessionStorage.removeItem(attemptEntry);
    } catch {
        return null;
    }

    if (stored === null || stored.length === 0 || stored.length > longestAttempt) {
        return null;
    }

    let parsed: unknown;

    try {
        parsed = JSON.parse(stored);
    } catch {
        return null;
    }

    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        return null;
    }

    const written = parsed as Record<string, unknown>;
    const fields = [
        'state',
        'codeVerifier',
        'baseAddress',
        'issuer',
        'clientId',
        'resource',
        'redirectUri',
        'displayName',
    ] as const;

    for (const field of fields) {
        const value = written[field];

        if (typeof value !== 'string' || value.length === 0) {
            return null;
        }
    }

    return written as unknown as WrittenAttempt;
}
