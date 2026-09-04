// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailMutationOutcome, MailMutationRecord, MailMutationResult } from '@mailfathom/client-backend';

// What a change this client asked for is, and the rule deciding which of them converge on their own and which are
// somebody's to settle. It is stated here, as functions over values, rather than inside the provider that applies it,
// because it is the part of this feature a reader has to be able to check: a conflict resolved by last-write-wins is
// invisible in a screenshot and expensive in a mailbox.
//
// The rule in one paragraph. A change the deployment wrote down is followed until its record says otherwise. A record
// that reached the mail server, and one somebody took back, are both over and nothing is put in front of anybody: two
// clients asking a mailbox for the same thing is not a conflict, and neither is a change that simply landed. A record
// the account stopped retrying, and one whose command went out and was never answered, are the two the deployment
// cannot settle — each is put in front of the person with both sides, and neither is resolved by asking again or by
// dropping it until they say which.

/**
 * What the client asked a mailbox for, named for the person rather than for the record behind it.
 *
 * One member today because marking a message read is the one mailbox change this client makes. A second act arrives
 * here as a second member with its own sentences beside it, rather than as a free-form label a screen would have to
 * guess the wording for.
 */
export type ChangeAct = 'markRead';

/** One change the deployment wrote down, which this client follows until the mailbox agrees or somebody decides. */
export interface PendingChange {
    readonly recordId: string;
    readonly storedEmailId: string;
    readonly act: ChangeAct;
}

/**
 * Where a change stands once the deployment has been asked about its record.
 *
 * `waiting` and `converged` are the two nobody is told about. `exhausted` and `unanswered` are the two that are, and
 * they are separate members because they are separate sentences: one says the mailbox would not take the change, and
 * the other says nobody knows whether it did.
 */
export type ChangeStanding = 'waiting' | 'converged' | 'exhausted' | 'unanswered';

/** Whether this standing is one only the person can settle, which is what puts a change in front of them. */
export function isUndecided(standing: ChangeStanding): boolean {
    return standing === 'exhausted' || standing === 'unanswered';
}

/** Where the record says its change stands, under the rule this module opens with. */
export function standingOf(record: MailMutationRecord): ChangeStanding {
    // Read before the state, because it holds whatever that says: a placement command that went out and was never
    // answered leaves the message possibly in either folder, and a record marked completed over that is completed
    // about the command rather than about the mailbox.
    if (record.outcomeUnknown) {
        return 'unanswered';
    }

    switch (record.state) {
        case 'pending':
        case 'converging':
            return 'waiting';
        case 'completed':
            return 'converged';
        case 'cancelled':
            // Somebody took it back, here or from another client of the same mailbox. Nothing is pending and nobody is
            // owed a question: a withdrawal is an act rather than a collision.
            return 'converged';
        case 'dead-lettered':
            return 'exhausted';
    }
}

/**
 * What one submission of one act became, which is the whole of what a producer hands the queue.
 *
 * The two callbacks are the producer's because only it knows what its own act means: asking again is that act
 * submitted afresh, and letting go is whatever the screen has to stop claiming. Holding them here is what keeps this
 * module from knowing about marking read, or about whatever the second act turns out to be.
 */
export interface ChangeSubmission {
    readonly act: ChangeAct;

    /** The messages the submission named, which is what an answer that never arrived is measured against. */
    readonly asked: readonly string[];

    /** What the deployment answered per message, or `null` where the submission never reached it. */
    readonly results: readonly MailMutationResult[] | null;

    /** Asks for the same change afresh over the messages named, which is what a retry and a resolution both do. */
    readonly askAgain: (storedEmailIds: readonly string[]) => void;

    /** Stops claiming the change over the messages named, which is what letting one go has to leave behind. */
    readonly letGo: (storedEmailIds: readonly string[]) => void;
}

/** What a submission's answer says: the records to follow, and the messages it wrote nothing down for. */
export interface SubmissionReading {
    readonly followed: readonly PendingChange[];

    /**
     * The messages the deployment refused, by what it refused them with.
     *
     * Grouped rather than listed, because what a person is owed is one sentence per reason naming how many messages it
     * happened to, rather than one card per message saying the same thing several times.
     */
    readonly refused: ReadonlyMap<MailMutationOutcome, readonly string[]>;
}

/** What to follow and what to report, for one submission the deployment answered. */
export function readSubmission(submission: ChangeSubmission): SubmissionReading {
    const followed: PendingChange[] = [];
    const refused = new Map<MailMutationOutcome, string[]>();

    for (const result of submission.results ?? []) {
        if (result.outcome !== 'recorded') {
            refused.set(result.outcome, [...(refused.get(result.outcome) ?? []), result.storedEmailId]);

            continue;
        }

        for (const change of result.changes) {
            followed.push({ recordId: change.recordId, storedEmailId: result.storedEmailId, act: submission.act });
        }
    }

    return { followed, refused };
}
