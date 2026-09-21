// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    AnswerBlock,
    ClientFailureReason,
    ClientResult,
    DiscoveryRetrievalProgress,
    DiscoveryRunCeilings,
    DiscoveryRunEnding,
    DiscoveryRunEvent,
    DiscoveryRunSpend,
    RunTail,
} from '@mailfathom/client-backend';

// What a run being followed amounts to on the screen, and how one read of it moves that on. It is a value and a
// function over it rather than part of the hook below, because what a read does to an answer is ordinary logic: the
// interesting cases are a read that failed, a run that had already finished, and a burst of blocks arriving in one
// answer, and none of them needs a component mounted to be stated.
//
// **A run that ends is a state rather than an absence.** Every ending the deployment publishes is carried as itself —
// the person stopping the run, a spend ceiling refusing it, and a fault are three different sentences and three
// different next steps — and so is the one ending the deployment cannot publish, a run it no longer holds. A screen
// handed one boolean could draw none of them, and a run that quietly stopped being drawn would have charged somebody
// for an answer they never saw.

/** One block of an answer, and where the run put it. */
export interface ArrivedAnswerBlock {
    /** The run's own sequence for the event that published the block, which is what orders the answer. */
    readonly sequence: number;

    readonly block: AnswerBlock;
}

/**
 * Why the last read of a run did not answer.
 *
 * It is the failure model minus the one reason that ends the following rather than interrupting it: a run the
 * deployment does not hold is an answer, and the four left each leave the run where it was and are told apart on the
 * screen because the way out of each of them is different.
 */
export type RunReadFailure = Exclude<ClientFailureReason, 'missing'>;

/**
 * How a run ended, from the screen's side of it.
 *
 * The deployment's own endings, plus the two it has no event for: a run that finished, and a run this deployment no
 * longer holds — which a read learns from a status rather than from anything the run wrote, and which is exactly the
 * case a screen must say out loud instead of letting the run disappear.
 */
export type RunEnding = DiscoveryRunEnding | 'completed' | 'gone';

/** What a run said about itself before it spent anything: what one question may spend here, and what will answer it. */
export interface RunEnvelope {
    readonly ceilings: DiscoveryRunCeilings;
    readonly endpointAlias: string;
    readonly publishedModel: string;
}

/** A run as far as the client has read it. */
export interface FollowedAnswer {
    /** The blocks that have arrived, in the order the run published them. */
    readonly blocks: readonly ArrivedAnswerBlock[];

    /** Whether more is still coming. */
    readonly running: boolean;

    /** The revision the run's plan was written against, and `null` before the run has said. */
    readonly planSchemaVersion: number | null;

    /** Why the last read did not answer, and `null` where it did, which is what a screen says instead of waiting in silence. */
    readonly failure: RunReadFailure | null;

    /** What the run may spend and what will answer it, and `null` before the run has started. */
    readonly envelope: RunEnvelope | null;

    /** How far retrieval has got, and `null` before a lookup of the plan has settled. */
    readonly retrieval: DiscoveryRetrievalProgress | null;

    /** What the run has consumed, and `null` before it has reported any. */
    readonly spend: DiscoveryRunSpend | null;

    /** How the run ended, and `null` while it is still working. */
    readonly ending: RunEnding | null;

    /** When a refused allowance returns, which only a period refusal names. */
    readonly retryAt: string | null;
}

/**
 * A run nothing has been read of yet.
 *
 * It is running, because a run being followed is one somebody has just asked for: a client that assumed otherwise would
 * draw a finished answer with nothing in it for as long as the first read takes.
 */
export const nothingRead: FollowedAnswer = {
    blocks: [],
    running: true,
    planSchemaVersion: null,
    failure: null,
    envelope: null,
    retrieval: null,
    spend: null,
    ending: null,
    retryAt: null,
};

/**
 * The answer after one read of the run.
 *
 * @param before What the client held before the read.
 * @param tail What the read answered.
 * @returns The answer to draw next.
 */
export function answerAfter(before: FollowedAnswer, tail: ClientResult<RunTail<DiscoveryRunEvent>>): FollowedAnswer {
    if (tail.outcome === 'failed') {
        // A run the deployment does not hold for this person is not a run to wait on: asking again reaches the same
        // answer for ever, so what has arrived is the whole of the answer, and the screen says which of the endings
        // this is rather than simply stopping. Every other failure leaves the run where it was and is carried as
        // itself rather than as one flag, because a session that expired, a grant that is missing, a deployment out of
        // reach and an answer this client could not read send somebody four different ways — and a screen handed one
        // boolean can only offer one of them.
        return tail.failure.reason === 'missing'
            ? { ...before, running: false, failure: null, ending: 'gone' }
            : { ...before, failure: tail.failure.reason };
    }

    const blocks = [...before.blocks];
    let planSchemaVersion = before.planSchemaVersion;
    let envelope = before.envelope;
    let retrieval = before.retrieval;
    let spend = before.spend;
    let ending = before.ending;
    let retryAt = before.retryAt;

    for (const event of tail.value.events) {
        if (event.kind === 'started') {
            planSchemaVersion = event.planSchemaVersion;
            envelope = {
                ceilings: event.ceilings,
                endpointAlias: event.endpointAlias,
                publishedModel: event.publishedModel,
            };
        } else if (event.kind === 'block') {
            blocks.push({ sequence: event.sequence, block: event.block });
        } else if (event.kind === 'retrieval') {
            retrieval = event.progress;
            spend = event.spend;
        } else if (event.kind === 'completed') {
            spend = event.spend;
            ending = 'completed';
        } else if (event.kind === 'failed') {
            spend = event.spend;
            ending = event.ending;
            retryAt = event.retryAt;
        }
    }

    return {
        blocks,
        running: tail.value.running,
        planSchemaVersion,
        failure: null,
        envelope,
        retrieval,
        spend,
        ending,
        retryAt,
    };
}
