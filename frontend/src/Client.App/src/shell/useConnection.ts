// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState, useSyncExternalStore } from 'react';
import {
    readDeploymentSession,
    readMailAccounts,
    type ClientResult,
    type DeploymentSession,
    type MailAccountDirectory,
} from '@mailfathom/client-backend';
import type { DeploymentTransport } from '../deployment/sendToDeployment';
import { offers } from './capabilities';

// What the client knows about the deployment it is signed in to: what the credential may do there, how current each of
// the owner's accounts is, and whether the deployment is answering at all. It is one thing rather than three because it
// arrives as one exchange — the grant decides whether the accounts are asked for at all, and a deployment that stopped
// answering stops both.
//
// The session is re-read on every attempt rather than kept from the sign-in that produced it. A grant is the
// deployment's to narrow while the client is open, and a client acting on the grant it was handed at sign-in would keep
// offering what the service has since begun refusing.

/** The most times the client reaches for a deployment that is not answering before it waits to be asked. */
export const mostReconnectionAttempts = 5;

const firstReconnectionDelay = 1_000;
const longestReconnectionDelay = 30_000;

/**
 * How long to wait before the attempt after this one, in milliseconds.
 *
 * The wait doubles and is capped, so a deployment that is down for a while is not asked hundreds of times, and the
 * spread keeps a fleet of clients that all lost the same deployment from coming back in step with each other.
 *
 * @param made How many automatic attempts have already been made since the last answer.
 * @param drawn A value in `[0, 1)`, which the caller draws so that this stays a function of its arguments.
 * @returns The delay to wait, between three quarters and five quarters of the nominal one for that attempt.
 */
export function reconnectionDelay(made: number, drawn: number): number {
    const nominal = Math.min(firstReconnectionDelay * 2 ** made, longestReconnectionDelay);

    return Math.round(nominal * (0.75 + drawn / 2));
}

/** What the deployment last said, and how current what is on the screen therefore is. */
export interface Connection {
    /** What the deployment says the credential may do, or `null` while that is being read for the first time on this attempt. */
    readonly session: ClientResult<DeploymentSession> | null;

    /** The owner's accounts, or `null` where they have not been read — which includes a credential not allowed to. */
    readonly accounts: ClientResult<MailAccountDirectory> | null;

    /** When the accounts on the screen were read, which is what every age beside one is measured from. */
    readonly readAt: Date | null;

    /** Whether this machine has a network at all, which is a different sentence from a deployment that is not answering. */
    readonly online: boolean;

    /** How many automatic attempts have been made since the deployment last answered. */
    readonly attempts: number;

    /** Reads everything again, from a person asking rather than from the client trying on its own. */
    readonly reread: () => void;
}

/**
 * What one attempt answered, tagged with the attempt and the identity it answered for.
 *
 * The tag is what makes "still waiting" a thing this hook works out during a render rather than a second piece of state
 * set beside the first: an answer that is not the current attempt's is a stale answer, and clearing it in the effect
 * that starts the next read would be a render spent saying what the tag already says.
 *
 * The identity is half of that tag rather than the attempt alone, and it is the half that matters most: signing out and
 * back in as somebody else changes the credential without changing the attempt, so an answer tagged by attempt alone
 * would put the previous owner's accounts and the previous owner's grants in front of the next person for as long as
 * their own read takes. It holds the credential the frame is already holding rather than a second copy of anything, it
 * is compared and never read, and nothing renders it.
 */
interface Answered {
    readonly session: ClientResult<DeploymentSession> | null;
    readonly accounts: ClientResult<MailAccountDirectory> | null;
    readonly readAt: Date | null;
    readonly answering: number;
    readonly presentedAt: string | null;
    readonly presenting: string | null;
}

// Before the first attempt there is nothing, tagged with an attempt and an identity no read will ever carry.
const nothingRead: Answered = {
    session: null,
    accounts: null,
    readAt: null,
    answering: -1,
    presentedAt: null,
    presenting: null,
};

function subscribeToConnectivity(changed: () => void): () => void {
    window.addEventListener('online', changed);
    window.addEventListener('offline', changed);

    return () => {
        window.removeEventListener('online', changed);
        window.removeEventListener('offline', changed);
    };
}

function isOnline(): boolean {
    return window.navigator.onLine;
}

/**
 * Holds what the deployment says, and reaches for it again on its own while it says nothing.
 *
 * @param baseAddress Where the deployment is, or `null` where none has been adopted.
 * @param authorization The finished header value this client signed in with, or `null` where nobody is signed in.
 * @param send How a request reaches the deployment.
 * @param onCredentialRefused What to do about a credential the deployment has stopped accepting, which is the one
 * answer this hook reports rather than renders: it is acted on once, by whatever owns signing in, instead of producing
 * the same refusal on every later read.
 * @returns Everything a screen needs to say what it is looking at and how current it is.
 */
export function useConnection(
    baseAddress: string | null,
    authorization: string | null,
    send: DeploymentTransport,
    onCredentialRefused: () => void,
): Connection {
    const online = useSyncExternalStore(subscribeToConnectivity, isOnline);
    const [read, setRead] = useState(0);
    const [attempts, setAttempts] = useState(0);
    const [answered, setAnswered] = useState<Answered>(nothingRead);

    useEffect(() => {
        // Nothing is read without a network, and what was read before it went is left on the screen rather than
        // cleared: the last answer is still the truest thing anybody has, and saying so beside it is what the offline
        // state is for. Coming back re-runs this, which is the whole of the automatic recovery from that direction.
        if (baseAddress === null || authorization === null || !online) {
            return;
        }

        // Abandoning is what says an answer is nobody's to render any more, and it is one mechanism rather than two:
        // the signal already has to travel to the transport, so a second flag beside it would be a second thing to
        // keep true.
        const attempted = new AbortController();
        const credential = { baseAddress, authorization };
        const transport = send(attempted.signal);

        // Asked through a function rather than read off the controller, so nothing decides at the first check that it
        // can never be true at the second: what changes it is a cleanup running while a read is in flight.
        const abandoned = (): boolean => attempted.signal.aborted;

        void (async () => {
            const session = await readDeploymentSession(credential, transport);

            if (abandoned()) {
                return;
            }

            if (session.outcome === 'failed' && session.failure.reason === 'unauthenticated') {
                onCredentialRefused();

                return;
            }

            if (session.outcome === 'failed') {
                setAnswered({
                    session,
                    accounts: null,
                    readAt: null,
                    answering: read,
                    presentedAt: baseAddress,
                    presenting: authorization,
                });

                return;
            }

            setAttempts(0);

            // On the screen as soon as it is known rather than once the accounts beside it are: what it decides — the
            // spaces, the controls, the deployment's version — is answerable now, and holding it back would leave the
            // frame saying it is still reaching a deployment that has already answered.
            setAnswered({
                session,
                accounts: null,
                readAt: null,
                answering: read,
                presentedAt: baseAddress,
                presenting: authorization,
            });

            // A credential that may not read mail is never asked for it. The refusal would be the service's to give
            // and it would arrive as a failure on a screen, where what is true is that the client is not offering
            // something rather than that something went wrong.
            if (!offers(session.value, 'readMail')) {
                return;
            }

            const accounts = await readMailAccounts(credential, transport);

            if (abandoned()) {
                return;
            }

            if (accounts.outcome === 'failed' && accounts.failure.reason === 'unauthenticated') {
                onCredentialRefused();

                return;
            }

            setAnswered({
                session,
                accounts,
                readAt: new Date(),
                answering: read,
                presentedAt: baseAddress,
                presenting: authorization,
            });
        })();

        return () => {
            attempted.abort();
        };
    }, [baseAddress, authorization, online, read, send, onCredentialRefused]);

    // An answer belonging to an earlier attempt, or to a credential this client is no longer signed in with, is not
    // this attempt's, and nothing on the screen may be drawn from it: what a person is looking at then is a read in
    // flight, which is what the frame says while it waits.
    const current =
        answered.answering === read && answered.presentedAt === baseAddress && answered.presenting === authorization;
    const connection = current ? answered : nothingRead;

    // Only a deployment that did not answer is reached for again. A credential it refused, a grant it does not hold,
    // and an answer this client could not read each repeat identically however many times they are asked for, so the
    // budget is spent on the one failure that passes on its own.
    const lost =
        connection.session?.outcome === 'failed' && connection.session.failure.reason === 'unavailable' && online;

    useEffect(() => {
        if (!lost || attempts >= mostReconnectionAttempts) {
            return;
        }

        const waiting = setTimeout(
            () => {
                setAttempts((made) => made + 1);
                setRead((token) => token + 1);
            },
            reconnectionDelay(attempts, Math.random()),
        );

        return () => {
            clearTimeout(waiting);
        };
    }, [lost, attempts]);

    return {
        session: connection.session,
        accounts: connection.accounts,
        readAt: connection.readAt,
        online,
        attempts,
        reread: () => {
            setAttempts(0);
            setRead((token) => token + 1);
        },
    };
}
