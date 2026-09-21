// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    AnswerBlock,
    ClientFailureReason,
    ClientResult,
    DiscoveryRunEvent,
    RunTail,
} from '@mailfathom/client-backend';

// What a run being followed amounts to on the screen, and how one read of it moves that on. It is a value and a
// function over it rather than part of the hook below, because what a read does to an answer is ordinary logic: the
// interesting cases are a read that failed, a run that had already finished, and a burst of blocks arriving in one
// answer, and none of them needs a component mounted to be stated.

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
        // answer for ever, so what has arrived is the whole of the answer. Every other failure leaves the run where it
        // was and is carried as itself rather than as one flag, because a session that expired, a grant that is
        // missing, a deployment out of reach and an answer this client could not read send somebody four different
        // ways — and a screen handed one boolean can only offer one of them.
        return tail.failure.reason === 'missing'
            ? { ...before, running: false, failure: null }
            : { ...before, failure: tail.failure.reason };
    }

    const blocks = [...before.blocks];
    let planSchemaVersion = before.planSchemaVersion;

    for (const event of tail.value.events) {
        if (event.kind === 'started') {
            planSchemaVersion = event.planSchemaVersion;
        } else if (event.kind === 'block') {
            blocks.push({ sequence: event.sequence, block: event.block });
        }
    }

    return { blocks, running: tail.value.running, planSchemaVersion, failure: null };
}
