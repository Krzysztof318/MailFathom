// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The mailbox as rows: one page of a folder, one page of a search, and one conversation. The three are together because
// they are one shape — the service publishes a search result as a list row with two fields added, and a conversation's
// `email` as that same row field for field — so a fixture that stated them apart would be three copies of one thing
// drifting away from each other.
//
// Nothing here parses a request. Where an answer depends on what was asked for — how far into the folder somebody has
// read — the corpus states it as a function of that question, and the consumer routing it decides what was asked.

import { markupOnlyId, markupOnlySubject, newsletterId, newsletterSubject } from './messages';

/**
 * How many messages the folder behind the corpus holds.
 *
 * It is the number `frontend/src/AGENTS.md` names as the one the client actually has to render, which is why the rows
 * are generated from their own position rather than written out: a fixture of two hundred and fourteen thousand
 * literals would prove the same thing and be unreadable, and nothing in one row is anybody's mail.
 */
export const mailboxSize = 214_000;

/** How many rows one page of the folder holds, which is what a cursor moves by. */
export const rowsPerPage = 100;

/** The conversation every corpus message that belongs to one belongs to. */
export const conversationId = '00000000-0000-4000-8000-0000000000c0';

/**
 * What a derivation concluded about the rows that carry one.
 *
 * Not every row, and that is deliberate — a corpus whose every message carried a reading could not show the state a
 * message no derivation reached is drawn in, which is an ordinary row rather than a gap. The first row carries none so
 * that a check reaching a row by what it is about still matches on the mail alone.
 *
 * @param at Which position in the answer, which decides whether it carries one at all and what it says. A folder page
 * counts rows and a conversation counts messages, and the derivation is the same shape on both.
 */
function derivedReading(at: number) {
    if (at % 4 !== 1) {
        return null;
    }

    const owed = at % 8 === 1;

    return {
        derivedAt: '2026-08-31T09:42:00+00:00',
        marks: [
            owed
                ? {
                      aspect: 'Commitment',
                      text: 'An answer is owed before the end of the week.',
                      reason: 'The message asks for confirmation and names a day.',
                      dueAt: '2026-09-04T16:00:00+00:00',
                      source: 'Model',
                      origin: 'agents/reader',
                      evidence: [`fragment-${String(at)}-1`],
                  }
                : {
                      aspect: 'Sense',
                      text: 'A delivery note for an order already placed.',
                      reason: 'The sender names an order number the message is about.',
                      dueAt: null,
                      source: 'DeterministicRule',
                      origin: 'rules/delivery-note',
                      evidence: [`fragment-${String(at)}-1`, `fragment-${String(at)}-2`],
                  },
        ],
    };
}

function timelineRow(at: number) {
    return {
        id: `message-${String(at)}`,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: `Message ${String(at)}`,
        receivedAt: '2026-08-31T09:41:00+00:00',
        sentAt: null,
        senderAddress: `writer-${String(at)}@nordwind.example`,
        senderDisplayName: `Writer ${String(at)}`,
        toAddresses: ['user@example.invalid'],
        unread: at % 3 === 0,
        flagged: false,
        answered: false,
        hasAttachments: at % 5 === 0,
        attachmentCount: at % 5 === 0 ? 1 : 0,
        sizeOctets: 4_096,
        preview: `The opening of message ${String(at)}.`,
        threadMessageCount: null,
    };
}

/**
 * One row of a folder, which is a row plus what a derivation concluded about the message.
 *
 * Apart from {@link timelineRow} because the search route publishes no derivation at all — not the field answering
 * nothing, the field absent — and a search result is that same row with two fields added. A corpus that carried one
 * into a search answer would be stating the service answering with something it never answers.
 */
function folderRow(at: number) {
    return { ...timelineRow(at), enrichment: derivedReading(at) };
}

/**
 * Where in the folder the one row standing for a correspondence sits.
 *
 * A folder whose every row is a single message draws none of the count the design puts on a row that stands for an
 * exchange, so a corpus without one cannot show that state at all. It is far enough down that the rows a check reaches
 * by position are the plain ones they were, and near enough the top to be on the first screen of every composition.
 */
const conversationRowPosition = 3;

// The row that opens the conversation below, drawn in the folder it arrived in. It is composed here rather than held
// as a constant because what it is built from is declared further down this file.
function conversationTimelineRow() {
    return {
        ...folderRow(conversationRowPosition),
        id: rackingQuote.id,
        threadId: conversationId,
        threadMessageCount: conversationRows.length,
        subject: rackingQuote.subject,
        senderAddress: 'sales@nordwind.example',
        senderDisplayName: 'Nordwind',
        preview: rackingQuote.preview,
    };
}

/**
 * One page of the folder, keyset-paged the way the client surface pages it.
 *
 * The cursor is the row the page starts at, written as text, because what a check about a cursor proves is that the
 * client holds one and continues from it — what a deployment encodes in one is the deployment's own business.
 *
 * @param from The row the page starts at, which is what a cursor names.
 */
export function timelinePage(from: number) {
    const start = Math.max(from, 0);
    const rows = Math.max(Math.min(rowsPerPage, mailboxSize - start), 0);

    return {
        emails: Array.from({ length: rows }, (_, at) =>
            start + at === conversationRowPosition ? conversationTimelineRow() : folderRow(start + at),
        ),
        nextCursor: start + rows >= mailboxSize ? null : String(start + rows),
        previousCursor: start === 0 ? null : String(start),
        pageSize: rowsPerPage,
    };
}

/**
 * What following a mark's evidence answers with, one resolution per citation and in the order they were asked about.
 *
 * The last of several is left unresolvable deliberately: a corpus that resolved every passage could not show the state
 * a reading whose message has been re-cut since is drawn in, which is a sentence saying so rather than a blank.
 *
 * @param fragments The passages the request named, which the answer is paired against by position.
 */
export function citationResolutions(fragments: readonly string[]) {
    return {
        citations: fragments.map((fragment, at) =>
            at > 0 && at === fragments.length - 1
                ? { outcome: 'Unresolvable', fragment: null }
                : {
                      outcome: 'Resolved',
                      fragment: {
                          fragmentId: fragment,
                          ordinal: at,
                          text: 'Please confirm the bays you want before the end of the week.',
                      },
                  },
        ),
    };
}

/**
 * What a folder holding nothing answers with.
 *
 * A screen cannot be scrolled into this state, so it is stated rather than derived: an empty folder says why it is
 * empty and what would fill it, and that sentence is only ever seen by a check that asked for this page.
 */
export const emptyFolderPage = {
    emails: [],
    nextCursor: null,
    previousCursor: null,
    pageSize: rowsPerPage,
};

// The message the conversation below opens with, named here because a search finds it too: one message written in two
// places would be the drift this directory exists to end, in miniature.
const rackingQuote = {
    id: '00000000-0000-4000-8000-0000000000c1',
    subject: 'The racking quote',
    preview: 'Two prices, one for the shallow bays and one for the deep ones.',
};

/**
 * What the search route answers, ranked both ways.
 *
 * The three rows are the three things a screen draws differently, which is what makes them worth three rows: a result
 * the query reached inside a file it carries names the file and where in it, one found by its words carries the
 * extracts around them, and one found by meaning alone carries neither and would otherwise read as an unexplained row.
 *
 * The file the first of them cites is the one the newsletter message in `messages.ts` actually carries, at the position
 * that message publishes it at, because a citation nothing behind it answers for is a coordinate a reader cannot follow.
 *
 * No row here carries a derivation, and that is the route rather than a gap in the corpus: the search endpoint
 * publishes no such field at all, which is why these are built from `timelineRow` rather than from `folderRow`.
 */
export const searchResults = {
    results: [
        {
            ...timelineRow(0),
            id: newsletterId,
            subject: newsletterSubject,
            senderAddress: 'news@example.invalid',
            senderDisplayName: 'Example',
            preview: 'A newsletter, as words.',
            snippets: [],
            matchedBy: 'BothRankings',
            attachmentMatches: [
                {
                    attachmentPosition: 0,
                    fileName: 'orders.csv',
                    mediaType: 'text/csv',
                    source: 'Document',
                    segmentKind: null,
                    segmentNumber: null,
                    extracts: ['The **renewal** falls due at the end of the month'],
                },
            ],
            isDepictedMatch: false,
        },
        {
            ...timelineRow(1),
            id: markupOnlyId,
            subject: markupOnlySubject,
            senderAddress: 'dispatch@kettles.invalid',
            senderDisplayName: 'Kettles',
            preview: 'Your order left this morning and should reach you on Thursday.',
            snippets: ['what the **renewal** covers'],
            matchedBy: 'LexicalRanking',
            attachmentMatches: [],
            isDepictedMatch: false,
        },
        {
            ...timelineRow(2),
            id: rackingQuote.id,
            threadId: conversationId,
            subject: rackingQuote.subject,
            senderAddress: 'sales@nordwind.example',
            senderDisplayName: 'Nordwind',
            preview: rackingQuote.preview,
            snippets: [],
            matchedBy: 'SemanticRanking',
            attachmentMatches: [],
            isDepictedMatch: false,
        },
    ],
    nextCursor: null,
    pageSize: 20,
    retrievalMode: 'Hybrid',
    semanticSearch: 'Available',
    includedJunkMail: false,
};

/** What a search that matched nothing answers with, which is a page rather than a refusal. */
export const noSearchResults = {
    results: [],
    nextCursor: null,
    pageSize: 20,
    retrievalMode: 'Hybrid',
    semanticSearch: 'Available',
    includedJunkMail: false,
};

const conversationRows = [
    {
        id: rackingQuote.id,
        answersRow: null,
        folder: 'INBOX',
        subject: rackingQuote.subject,
        sentAt: '2026-08-28T08:12:00+00:00',
        senderAddress: 'sales@nordwind.example',
        senderDisplayName: 'Nordwind',
        preview: rackingQuote.preview,
    },
    {
        id: '00000000-0000-4000-8000-0000000000c2',
        answersRow: 0,
        folder: 'SENT',
        subject: 'Re: The racking quote',
        sentAt: '2026-08-28T09:40:00+00:00',
        senderAddress: 'user@example.invalid',
        senderDisplayName: 'Iris Marlow',
        preview: 'Does the deeper price include the fixings?',
    },
    {
        id: '00000000-0000-4000-8000-0000000000c3',
        answersRow: 1,
        folder: 'INBOX',
        subject: 'Re: The racking quote',
        sentAt: '2026-08-29T07:55:00+00:00',
        senderAddress: 'sales@nordwind.example',
        senderDisplayName: 'Nordwind',
        preview: 'It does, and the delivery is inside the same figure.',
    },
    {
        id: '00000000-0000-4000-8000-0000000000c4',
        answersRow: 2,
        folder: 'SENT',
        subject: 'Re: The racking quote',
        sentAt: '2026-08-29T16:04:00+00:00',
        senderAddress: 'user@example.invalid',
        senderDisplayName: 'Iris Marlow',
        preview: 'Then take the deeper bays. I will confirm the dates on Monday.',
    },
    {
        id: '00000000-0000-4000-8000-0000000000c5',
        answersRow: 3,
        folder: 'INBOX',
        subject: 'Re: The racking quote',
        sentAt: '2026-08-31T08:03:00+00:00',
        senderAddress: 'sales@nordwind.example',
        senderDisplayName: 'Nordwind',
        preview: 'Booked for the ninth. The crew will need the yard for a morning.',
    },
];

/**
 * Where that conversation stands, as a deployment that turned the derivation on would have written it down.
 *
 * Three statements, one of each aspect a correspondence of this kind produces, each resting on the message that says
 * it — which is what the block is for, and what a capture of the screen has to show. A version difference is absent
 * because nothing in this exchange carries a document.
 */
export const conversationState = {
    threadId: conversationId,
    coverage: 'WholeThread',
    derivedAt: '2026-08-31T08:20:00+00:00',
    entries: [
        {
            aspect: 'Agreement',
            text: 'The deeper bays, with the fixings and the delivery inside the same figure.',
            owedBy: null,
            dueAt: null,
            sources: [{ kind: 'email', email: conversationRows[2]?.id ?? '' }],
        },
        {
            aspect: 'OpenQuestion',
            text: 'Whether the yard can be spared for a morning on the ninth.',
            owedBy: null,
            dueAt: null,
            sources: [{ kind: 'email', email: conversationRows[4]?.id ?? '' }],
        },
        {
            aspect: 'Commitment',
            text: 'Confirm the dates on Monday.',
            owedBy: 'Iris Marlow',
            dueAt: '2026-09-01T08:00:00+00:00',
            sources: [{ kind: 'email', email: conversationRows[3]?.id ?? '' }],
        },
    ],
};

/**
 * One conversation, long enough that a screen collapses it.
 *
 * The screen shows the latest message and folds every earlier one behind a control naming how many there are, so a
 * conversation of two would draw the collapsed history and prove nothing about it. Five is the shortest one where the
 * fold is worth reading: four messages behind the control, in both folders the exchange ran through.
 *
 * Each message carries what a derivation concluded about it, because the thread route publishes a message in the same
 * shape a list row is published in — the field and all — and a corpus that left it off would be stating this route
 * answering like the search route, which is the one route that genuinely publishes no derivation. Only one of the five
 * carries a reading, for the reason {@link derivedReading} gives about a folder page.
 */
export const conversation = {
    threadId: conversationId,
    messages: conversationRows.map((row, position) => ({
        position,
        answeredId: row.answersRow === null ? null : (conversationRows[row.answersRow]?.id ?? null),
        email: {
            id: row.id,
            account: 'work',
            folder: row.folder,
            threadId: conversationId,
            subject: row.subject,
            receivedAt: row.sentAt,
            sentAt: row.sentAt,
            senderAddress: row.senderAddress,
            senderDisplayName: row.senderDisplayName,
            toAddresses: [row.folder === 'SENT' ? 'sales@nordwind.example' : 'user@example.invalid'],
            unread: false,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            sizeOctets: 3_120,
            preview: row.preview,
            enrichment: derivedReading(position),
            threadMessageCount: conversationRows.length,
        },
    })),
    participants: [
        { address: 'sales@nordwind.example', displayName: 'Nordwind', messageCount: 3 },
        { address: 'user@example.invalid', displayName: 'Iris Marlow', messageCount: 2 },
    ],
    messageCount: conversationRows.length,
    moreMessagesNotAssembled: false,
    moreParticipantsNotNamed: false,
    nextCursor: null,
    pageSize: 25,
};
