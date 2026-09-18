// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { conversationId } from './mail';

// The signed-in person's address book, as the two books the client surface publishes it as: the people they wrote down
// themselves and the people their own mailboxes picked up from mail that arrived.
//
// `frontend/tests/AGENTS.md` § *The corpus* holds what the whole of it is and what may go in it. What this file adds
// beside the resting book is the states the People screen has to be looked at in and cannot be reached by scrolling: a
// book with nobody in it, a record collected rather than asserted, a write the book refused, and a person the mailbox
// has nothing to say about.
//
// Nobody here exists. Every name is invented and every host is a reserved one, exactly as it is for the mail.

/** How many people one page of a book holds, which is what a cursor moves by. */
export const contactsPerPage = 50;

function person(at: number, origin: string) {
    return {
        id: `contact-${String(at)}`,
        displayName: `Correspondent ${String(at)}`,
        addresses: [`correspondent-${String(at)}@nordwind.example`],
        preferredAddress: `correspondent-${String(at)}@nordwind.example`,
        note: null,
        origin,
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
    };
}

/**
 * One person this user wrote down, named rather than numbered.
 *
 * The book's first row is the one every check reaching a contact by position lands on, so it is the record a screen is
 * most often looked at with — a person with two addresses and something written about them, which is what the design
 * draws a contact's own page with.
 */
export const assertedContact = {
    id: 'contact-anna',
    displayName: 'Anna Marlow',
    addresses: ['anna@nordwind.example', 'a.marlow@nordwind.example'],
    preferredAddress: 'anna@nordwind.example',
    note: 'Runs the warehouse in Gdańsk.',
    origin: 'Asserted',
    recordedAt: '2026-06-02T11:15:00+00:00',
    amendedAt: '2026-08-14T08:02:00+00:00',
};

/**
 * One person a mailbox picked up, which is the record nobody may amend.
 *
 * It is the second state a contact is drawn in and cannot be reached from the asserted one by any press, so the book
 * that holds it is a page of its own rather than an extra row in the resting one.
 */
export const collectedContact = {
    id: 'contact-bartosz',
    displayName: 'Bartosz Rowe',
    addresses: ['b.rowe@contoso.example'],
    preferredAddress: 'b.rowe@contoso.example',
    note: null,
    origin: 'Collected',
    recordedAt: '2026-08-29T16:40:00+00:00',
    amendedAt: '2026-08-29T16:40:00+00:00',
};

/** The book of people this user wrote down, long enough that the list windows rather than drawing every row. */
export const assertedContactPage = {
    contacts: [assertedContact, ...Array.from({ length: contactsPerPage - 1 }, (_, at) => person(at + 1, 'Asserted'))],
    nextCursor: null,
};

/** The book of people the mailboxes picked up, which is the larger of the two against any real mailbox. */
export const collectedContactPage = {
    contacts: [
        collectedContact,
        ...Array.from({ length: contactsPerPage - 1 }, (_, at) => person(at + 100, 'Collected')),
    ],
    nextCursor: null,
};

/** A book with nobody in it, which is a state neither of the two above can be scrolled into. */
export const emptyContactPage = { contacts: [], nextCursor: null };

/** What writing somebody down answers with, which is the record the caller itself stated. */
export const contactWritten = { outcome: 'Written', contact: assertedContact, addressHolder: null };

/**
 * What a write claiming an address somebody else already holds answers with.
 *
 * The refusal names who holds it and nothing else about them — a client that could read a name out of a refusal would
 * be reading a record its caller was not given.
 */
export const contactAddressHeld = {
    outcome: 'AddressHeldByAnotherContact',
    contact: null,
    addressHolder: assertedContact.id,
};

/** What taking a collected record on answers with, which is that person as one this user now asserts. */
export const contactPromoted = {
    outcome: 'Written',
    contact: { ...collectedContact, origin: 'Asserted' },
    addressHolder: null,
};

/** What erasing one person answers with, which carries no name and no address by design. */
export const contactErased = { contact: assertedContact.id, wasHeld: true, addressesErased: 1 };

/**
 * What the mailbox has to say about one person, which is computed over the mail index rather than stored.
 *
 * The conversation is the one the mail corpus already holds, so opening a row from a person's page lands on a
 * correspondence rather than on a message the deployment would not answer for.
 */
export const contactCorrespondence = {
    contactId: assertedContact.id,
    threads: [
        {
            threadId: conversationId,
            latestMessageId: 'message-3',
            subject: 'Racking quote — 12 bays',
            lastCorrespondedAt: '2026-08-31T09:41:00+00:00',
        },
        {
            threadId: 'thread-annex',
            latestMessageId: 'message-11',
            subject: null,
            lastCorrespondedAt: '2026-07-18T13:04:00+00:00',
        },
    ],
    documents: [
        {
            messageId: 'message-3',
            position: 1,
            fileName: 'racking-quote.pdf',
            mediaType: 'application/pdf',
            receivedAt: '2026-08-31T09:41:00+00:00',
        },
        {
            messageId: 'message-11',
            position: 2,
            fileName: null,
            mediaType: 'image/png',
            receivedAt: '2026-07-18T13:04:00+00:00',
        },
    ],
};

/** A person the mail index found nothing about, which is the empty state both columns of their page have. */
export const noContactCorrespondence = { contactId: collectedContact.id, threads: [], documents: [] };
