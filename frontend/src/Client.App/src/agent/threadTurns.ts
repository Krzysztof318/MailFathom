// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    AgentAnswerOutcome,
    AgentConversationEntry,
    AgentMessageScope,
    AgentProposalState,
    AnswerBlock,
    DeclaredSource,
} from '@mailfathom/client-backend';
import type { ProposalPhase } from '../answerCanvas/proposalAnswering';

// What a conversation reads as, folded out of the entries it was written as. The record is flat — a status, a citation
// and a block each name the answer they belong to — and the screen draws turns, so this is the one place that decides
// which entries make one answer and which proposal a resolution decides.

/** One block of an answer, and the phase it stands in where it is a proposal rather than a reading. */
export interface AnsweredBlock {
    readonly sequence: number;
    readonly block: AnswerBlock;
    readonly phase: ProposalPhase | null;
}

/** One turn of the thread, in the order it was written. */
export type ThreadTurn =
    | {
          readonly kind: 'question';
          readonly sequence: number;
          readonly text: string;
      }
    | {
          /** A line the agent wrote on its own account rather than as an answer, which is the note a stop leaves. */
          readonly kind: 'note';
          readonly sequence: number;
          readonly text: string;
          readonly afterStop: boolean;
      }
    | {
          readonly kind: 'answer';
          readonly sequence: number;
          readonly run: string;

          /** What the question this answers was about, and `null` where it was about the whole of the mail. */
          readonly scope: AgentMessageScope | null;

          readonly blocks: readonly AnsweredBlock[];
          readonly sources: ReadonlyMap<string, DeclaredSource>;

          /** What the answer said it was doing last, and `null` before it has said anything. */
          readonly status: string | null;

          /** How it ended, and `null` while it is still being composed. */
          readonly ending: AgentAnswerOutcome | null;

          /** What the agent suggests asking next, which only an ending carries, and `null` before the answer ended. */
          readonly followUps: FollowUps | null;
      };

/** The questions an answer's ending suggests asking next, and where in the conversation that ending was written. */
export interface FollowUps {
    readonly sequence: number;
    readonly questions: readonly string[];
}

/** One answer of the thread. */
export type Answer = Extract<ThreadTurn, { kind: 'answer' }>;

/**
 * The turns a conversation's entries make.
 *
 * @param entries Everything read of the conversation, in sequence order.
 */
export function threadOf(entries: readonly AgentConversationEntry[]): readonly ThreadTurn[] {
    const turns: ThreadTurn[] = [];
    const answers = new Map<string, number>();
    const phases = new Map<number, AgentProposalState>();

    let lastScope: AgentMessageScope | null = null;
    let lastEnding: AgentAnswerOutcome | null = null;

    for (const entry of entries) {
        if (entry.kind === 'resolution') {
            phases.set(entry.proposedAt, entry.state);
        }
    }

    const revise = (run: string, change: (answer: Answer) => Answer): void => {
        const at = answers.get(run);
        const turn = at === undefined ? undefined : turns[at];

        if (at !== undefined && turn?.kind === 'answer') {
            turns[at] = change(turn);
        }
    };

    for (const entry of entries) {
        switch (entry.kind) {
            case 'message':
                if (entry.author === 'person') {
                    lastScope = entry.scope;
                    turns.push({ kind: 'question', sequence: entry.sequence, text: entry.text });
                } else {
                    turns.push({
                        kind: 'note',
                        sequence: entry.sequence,
                        text: entry.text,
                        afterStop: lastEnding === 'stopped',
                    });
                }

                break;

            case 'answerStarted':
                answers.set(entry.run, turns.length);
                turns.push({
                    kind: 'answer',
                    sequence: entry.sequence,
                    run: entry.run,
                    scope: lastScope,
                    blocks: [],
                    sources: new Map(),
                    status: null,
                    ending: null,
                    followUps: null,
                });

                break;

            case 'status':
                revise(entry.run, (answer) => ({ ...answer, status: entry.status }));

                break;

            case 'citation':
                revise(entry.run, (answer) => ({
                    ...answer,
                    sources: new Map(answer.sources).set(entry.source.id, entry.source),
                }));

                break;

            case 'block':
            case 'proposal':
                revise(entry.run, (answer) => ({
                    ...answer,
                    blocks: [
                        ...answer.blocks,
                        {
                            sequence: entry.sequence,
                            block: entry.block,
                            phase: entry.kind === 'proposal' ? (phases.get(entry.sequence) ?? 'pending') : null,
                        },
                    ],
                }));

                break;

            case 'answerEnded':
                lastEnding = entry.outcome;
                revise(entry.run, (answer) => ({
                    ...answer,
                    ending: entry.outcome,
                    followUps: { sequence: entry.sequence, questions: entry.followUps },
                }));

                break;

            case 'resolution':
            case 'other':
                break;
        }
    }

    return turns;
}

/**
 * What to offer asking next: what the thread's last turn suggested, where that turn is an answer that ended. A question
 * or a note written after it means the conversation has moved on, and an answer still being composed has suggested
 * nothing yet. `null` where there is nothing to offer.
 */
export function proposedNext(turns: readonly ThreadTurn[]): FollowUps | null {
    const last = turns.at(-1);

    return last?.kind === 'answer' && last.followUps !== null && last.followUps.questions.length > 0
        ? last.followUps
        : null;
}

/** The answer still being composed, which is the last one where it has not ended, and `null` where none is. */
export function answerInFlight(turns: readonly ThreadTurn[]): Answer | null {
    for (let at = turns.length - 1; at >= 0; at -= 1) {
        const turn = turns[at];

        if (turn?.kind === 'answer') {
            return turn.ending === null ? turn : null;
        }
    }

    return null;
}
