// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { newsletterId } from './messages';

// Two conversations with the Agent, as `/api/client` serves them: the history listing both, one conversation that was
// asked something and answered, and one whose answer is still being composed. It is what the Agent screen is looked at
// with, and `frontend/tests/AGENTS.md` § *The corpus* holds what may go in it.
//
// **The answered conversation cites a message this corpus already holds**, so following its citation reaches the
// reading pane rather than a message nothing can read. It also proposes an event, a task, a reply and a step in the
// mailbox — every kind of proposal the client draws — the task already accepted, so the proposal cards are looked at
// both waiting on somebody and answered. The one still composing has said what it is doing and drawn nothing yet, which
// is the state the status line exists for and none of the other screens can show.
//
// **Its answer is Markdown, and part of it is somebody else's.** A model writes structure — a list, an emphasis — and it
// also repeats what the mail it read said, which is how a sender's markup reaches an answer: so the answer carries a
// list beside raw HTML and a picture, each naming a host the reader never chose. The screen draws the first as structure
// and the other two as nothing that fetches, which is what the corpus has to hold for either to be looked at.

/** The conversation that was asked something and answered. */
export const answeredConversationId = '0198f4a1-0000-7000-8000-00000000a9e1';

/** The conversation whose answer is still being composed. */
export const composingConversationId = '0198f4a1-0000-7000-8000-00000000a9e2';

/** The question the answered conversation opened with, which is also the answer's name. */
export const answeredQuestionId = '0198f4a1-0000-7000-8000-00000000a9f1';

/** The question the composing conversation is answering, which is the run a steer or a stop names. */
export const composingQuestionId = '0198f4a1-0000-7000-8000-00000000a9f2';

/**
 * The history, the conversations being worked in first and then the one put away, each group newest activity first.
 *
 * The archived conversation is what the history's archive section is looked at with. It is read as the answered one,
 * which is where any conversation this corpus does not name reads.
 */
export const agentHistory = {
    conversations: [
        {
            id: composingConversationId,
            title: 'What is waiting on me today',
            startedAt: '2026-08-31T09:50:00+00:00',
            lastActivityAt: '2026-08-31T09:51:00+00:00',
            archived: false,
        },
        {
            id: answeredConversationId,
            title: 'How many bays were confirmed',
            startedAt: '2026-08-31T09:40:00+00:00',
            lastActivityAt: '2026-08-31T09:42:00+00:00',
            archived: false,
        },
        {
            id: '0198f4a1-0000-7000-8000-00000000a9e3',
            title: 'Invoice 08/2026 — what is missing',
            startedAt: '2026-08-24T08:10:00+00:00',
            lastActivityAt: '2026-08-24T08:14:00+00:00',
            archived: true,
        },
    ],
};

const current = { staleness: 'Current', observedAt: '2026-08-31T09:42:00+00:00' };

/** The host the answer's repeated markup names, which nothing the reader does should ever reach. */
export const answerMarkupHost = 'pixel.example';

/**
 * The raw HTML the answer repeats out of the mail it read, which is drawn as the text it is.
 *
 * Its address carries no scheme, as a tag's is free not to, so what stays text is the tag whole: an address with one
 * would be drawn as a warned link the way one in the prose is, which is a different thing from a tag becoming an element.
 */
export const answerRawMarkup = `<img src="//${answerMarkupHost}/bays.gif" alt="">`;

/** The places the answer lists, which are drawn as a list rather than as the dashes they were written with. */
export const answerListed = ['Two on the north side', 'Two by the loading door'] as const;

// What each conversation was written as. The contract records an entry as the JSON the service stored rather than as
// fields of its own, so the entries stand apart from the pages that carry them.
const answeredEntries = [
    {
        sequence: 1,
        entry: {
            entry: 'message',
            messageId: answeredQuestionId,
            author: 'Person',
            text: 'How many bays were confirmed?',
            scope: { kind: 'Mailbox', subject: null },
        },
    },
    { sequence: 2, entry: { entry: 'answerStarted', messageId: answeredQuestionId } },
    {
        sequence: 3,
        entry: { entry: 'status', messageId: answeredQuestionId, status: 'Reading the confirmation' },
    },
    {
        sequence: 4,
        entry: {
            entry: 'citation',
            messageId: answeredQuestionId,
            citation: {
                id: 'c-1',
                target: { kind: 'email', email: newsletterId },
                label: 'A newsletter from Example',
                medium: 'Written',
            },
        },
    },
    {
        sequence: 5,
        entry: {
            entry: 'block',
            messageId: answeredQuestionId,
            block: {
                type: 'answer',
                evidence: { support: 'Supported', citations: ['c-1'], freshness: current },
                text: [
                    'Four bays were confirmed, held to the end of the week.',
                    answerListed.map((place) => `- **${place}**`).join('\n'),
                    `The confirmation closes with ${answerRawMarkup} and a plan: ![The bays](https://${answerMarkupHost}/plan.png)`,
                ].join('\n\n'),
                confidence: 'High',
            },
        },
    },
    {
        sequence: 6,
        entry: {
            entry: 'proposal',
            messageId: answeredQuestionId,
            block: {
                type: 'eventProposal',
                evidence: { support: 'Supported', citations: ['c-1'], freshness: current },
                title: 'Walk through the four bays',
                start: '2026-09-02T09:00:00+00:00',
                end: '2026-09-02T10:00:00+00:00',
                isAllDay: false,
            },
        },
    },
    {
        sequence: 7,
        entry: {
            entry: 'proposal',
            messageId: answeredQuestionId,
            block: {
                type: 'taskProposal',
                evidence: { support: 'Supported', citations: ['c-1'], freshness: current },
                title: 'Confirm the bays in writing',
                dueOn: '2026-09-04',
            },
        },
    },
    {
        sequence: 8,
        entry: {
            entry: 'proposal',
            messageId: answeredQuestionId,
            block: {
                type: 'draft',
                evidence: { support: 'Supported', citations: ['c-1'], freshness: current },
                recipients: ['bays@example.invalid'],
                subject: 'Re: four bays to the end of the week',
                body: 'Thank you — four bays until Friday suits us. We will confirm the times by Wednesday.',
                disposition: 'Composed',
            },
        },
    },
    {
        sequence: 9,
        entry: {
            entry: 'proposal',
            messageId: answeredQuestionId,
            block: {
                type: 'suggestedAction',
                evidence: { support: 'Supported', citations: ['c-1'], freshness: current },
                action: 'FlagEmail',
                reason: 'The confirmation asks for an answer before the end of the week.',
                impact: 'ChangesMailbox',
                requiresConfirmation: true,
            },
        },
    },
    {
        sequence: 10,
        entry: {
            entry: 'answerEnded',
            messageId: answeredQuestionId,
            outcome: 'Completed',
            followUps: ['Who confirmed the bays?', 'Draft a reply asking to hold them another week'],
        },
    },
    { sequence: 11, entry: { entry: 'resolution', proposedAt: 7, state: 'Accepted' } },
];

const composingEntries = [
    {
        sequence: 1,
        entry: {
            entry: 'message',
            messageId: composingQuestionId,
            author: 'Person',
            text: 'What is waiting on me today?',
            scope: null,
        },
    },
    { sequence: 2, entry: { entry: 'answerStarted', messageId: composingQuestionId } },
    {
        sequence: 3,
        entry: { entry: 'status', messageId: composingQuestionId, status: 'Looking through today’s mail' },
    },
];

/** The answered conversation, read from its beginning. */
export const answeredConversation = {
    title: 'How many bays were confirmed',
    startedAt: '2026-08-31T09:40:00+00:00',
    composing: false,
    moreFollows: false,
    entries: answeredEntries,
};

/** The conversation whose answer is still being composed, read from its beginning. */
export const composingConversation = {
    title: 'What is waiting on me today',
    startedAt: '2026-08-31T09:50:00+00:00',
    composing: true,
    moreFollows: false,
    entries: composingEntries,
};

/** What the deployment answers a posted question with: the answer it opened, which is named after the question. */
export function messagePosted(messageId: string): { messageId: string; runId: string; sequence: number } {
    return { messageId, runId: messageId, sequence: 1 };
}
