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
        toAddresses: ['owner@example.invalid'],
        unread: at % 3 === 0,
        flagged: false,
        answered: false,
        hasAttachments: at % 5 === 0,
        attachmentCount: at % 5 === 0 ? 1 : 0,
        sizeOctets: 4_096,
        preview: `The opening of message ${String(at)}.`,
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
        emails: Array.from({ length: rows }, (_, at) => timelineRow(start + at)),
        nextCursor: start + rows >= mailboxSize ? null : String(start + rows),
        previousCursor: start === 0 ? null : String(start),
        pageSize: rowsPerPage,
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
        senderAddress: 'owner@example.invalid',
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
        senderAddress: 'owner@example.invalid',
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
 * One conversation, long enough that a screen collapses it.
 *
 * The screen shows the latest message and folds every earlier one behind a control naming how many there are, so a
 * conversation of two would draw the collapsed history and prove nothing about it. Five is the shortest one where the
 * fold is worth reading: four messages behind the control, in both folders the exchange ran through.
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
            toAddresses: [row.folder === 'SENT' ? 'sales@nordwind.example' : 'owner@example.invalid'],
            unread: false,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            sizeOctets: 3_120,
            preview: row.preview,
        },
    })),
    participants: [
        { address: 'sales@nordwind.example', displayName: 'Nordwind', messageCount: 3 },
        { address: 'owner@example.invalid', displayName: 'Iris Marlow', messageCount: 2 },
    ],
    messageCount: conversationRows.length,
    moreMessagesNotAssembled: false,
    moreParticipantsNotNamed: false,
    nextCursor: null,
    pageSize: 25,
};
