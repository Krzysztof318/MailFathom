// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What the user is writing, and what becomes of one of them when it is sent.
//
// A draft here is the one in the user's own drafts folder, so there is nothing held only in the client for a fixture
// to stand in for: what a save answers with is the record itself, and that is what these values are.

/** The identity the draft below is addressed by, everywhere the corpus names one draft. */
export const draftId = '00000000-0000-4000-8000-0000000000d0';

/** A message of the author's own, addressed to two people and carrying one staged file. */
export const draft = {
    draftId,
    account: 'work',
    subject: 'The yard on the ninth',
    recipients: [
        { role: 'To', address: 'sales@nordwind.example', displayName: 'Nordwind' },
        { role: 'Cc', address: 'yard@example.invalid', displayName: null },
    ],
    attachments: [
        {
            attachmentId: '00000000-0000-4000-8000-0000000000d1',
            fileName: 'yard.pdf',
            mediaType: 'application/pdf',
            sizeOctets: 18_432,
        },
    ],
    revision: 3,
    sizeOctets: 21_504,
};

/**
 * A second draft, written as an answer to stored mail rather than as a message of its own.
 *
 * It is worth stating because the two are one type with two halves, and a screen listing them draws the same row for
 * both: what separates them is what the deployment derived — the subject and the threading — rather than anything the
 * author typed twice.
 */
export const answeringDraft = {
    draftId: '00000000-0000-4000-8000-0000000000d2',
    account: 'work',
    subject: 'Re: The racking quote',
    recipients: [{ role: 'To', address: 'sales@nordwind.example', displayName: 'Nordwind' }],
    attachments: [],
    revision: 1,
    sizeOctets: 1_120,
};

/**
 * What is unsent, newest first, as the records themselves rather than as a listing.
 *
 * The envelope a listing puts them in is left to whoever routes it, because nothing in `Client.Backend` reads that
 * route yet — and a corpus that invented one would be a shape nobody could check against the service, which is the
 * failure this whole directory exists to end rather than to move.
 */
export const drafts = [answeringDraft, draft];

/** What a save answers with, which is the record wrapped the way the surface is documented as free to wrap it. */
export const savedDraft = { draft };

/** What a send answers with once the message is queued: an identity to watch in the outbox and nothing else. */
export const queuedSend = { outgoingEmail: '00000000-0000-4000-8000-0000000000e0' };

/**
 * What a send answers with when a rule of the deployment refused the message that was written.
 *
 * The code is what a client matches rather than the sentence beside it: the sentence is written for an operator and is
 * not a contract, so a fixture whose refusal was recognised by its wording would prove a client nobody wrote.
 */
export const refusedSend = {
    errorCode: 59_001,
    detail: 'The content scanner refused this message.',
};
