// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    readCollectedContacts,
    readOwnContacts,
    type ClientFailureReason,
    type ClientSession,
    type Contact,
    type MailFathomTransport,
} from '@mailfathom/client-backend';

// One half of the address book as a screen holds it: the page or pages read so far, where the walk continues, and
// whether it is still going. The two halves are read the same way and are two calls of this rather than one reader with
// a filter, which is the shape the surface publishes — what somebody wrote down and what their own mailboxes picked up
// are two books.
//
// **Each book is read from the leading end when it is asked for**, rather than both being held at once. A tab is a
// deliberate move between two lists rather than a filter over one, and holding the other book's pages against the
// chance of coming back would be a copy of somebody's correspondents kept for a screen nobody is looking at.
//
// **A write is followed by reading the book again rather than by editing what is held.** Recording, promoting and
// erasing each change where a person sorts in the book — and a promotion moves them between the two books entirely —
// so a held list corrected in place would be this client's own opinion of an ordering the deployment owns.
//
// **What is held names the book and the credential it was read for**, and a reading for either of those having changed
// is derived away during the render that observes it rather than cleared by an effect. That is what keeps the tab a
// reader has just pressed from drawing the other one's rows for a frame.

/** Which half of the book a screen is reading. */
export type ContactBookName = 'own' | 'collected';

/** What one half of the book holds, and the two ways a screen asks it for more. */
export interface ContactBookInForce {
    /** The people read so far, in the order the deployment walks the book. */
    readonly contacts: readonly Contact[];

    /** Whether nothing has answered yet, which is the wait the list says it is in. */
    readonly reading: boolean;

    /** Whether a further page is on its way, which is the wait drawn at the foot of the rows already there. */
    readonly paging: boolean;

    /** Why the book did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Whether the walk has reached the end of the book, so there is nothing further to ask for. */
    readonly complete: boolean;

    /** Asks for the page after the ones held, which a reader reaching the foot of the list is what calls. */
    readonly readMore: () => void;

    /** Reads the book again from the leading end: the way out of a failure, and what every write is followed by. */
    readonly readAgain: () => void;
}

// What is held, beside which book and which credential it belongs to.
interface HeldBook {
    readonly book: ContactBookName;
    readonly session: ClientSession | null;
    readonly contacts: readonly Contact[];
    readonly cursor: string | null;
    readonly complete: boolean;
    readonly reading: boolean;
    readonly paging: boolean;
    readonly failure: ClientFailureReason | null;
}

function nothingRead(book: ContactBookName, session: ClientSession | null): HeldBook {
    return {
        book,
        session,
        contacts: [],
        cursor: null,
        complete: false,
        reading: session !== null,
        paging: false,
        failure: null,
    };
}

/**
 * Holds one half of the signed-in person's address book.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — in which case nothing is read
 * and the screen draws a book that never answered rather than an empty one.
 * @param transport How a request reaches the deployment.
 * @param book Which half of the book to read.
 * @returns What is held, and the two ways a screen asks for more of it.
 */
export function useContactBook(
    session: ClientSession | null,
    transport: MailFathomTransport,
    book: ContactBookName,
): ContactBookInForce {
    const [held, setHeld] = useState<HeldBook>(() => nothingRead(book, session));

    // Which read is the current one. A tab changed, a credential replaced, or a book read again while a page is still
    // in flight each leave an answer on its way that must not be drawn: the ordering is not guaranteed, and a stale
    // page appended to a book somebody has since left reads as a rendering defect rather than as a race.
    const latest = useRef(0);

    const readFrom = useCallback(
        async (cursor: string | null): Promise<void> => {
            if (session === null) {
                return;
            }

            const asked = ++latest.current;
            const read = book === 'own' ? readOwnContacts : readCollectedContacts;
            const answer = await read(session, transport, { cursor });

            if (asked !== latest.current) {
                return;
            }

            setHeld((standing) => {
                const carried =
                    standing.book === book && standing.session === session ? standing : nothingRead(book, session);

                if (answer.outcome === 'failed') {
                    return { ...carried, reading: false, paging: false, failure: answer.failure.reason };
                }

                return {
                    book,
                    session,
                    // Appended where this was a further page and replaced where it was the first, which is the one
                    // place the cursor a read started from decides what its answer means.
                    contacts: cursor === null ? answer.value.contacts : [...carried.contacts, ...answer.value.contacts],
                    cursor: answer.value.nextCursor,
                    complete: answer.value.nextCursor === null,
                    reading: false,
                    paging: false,
                    failure: null,
                };
            });
        },
        [book, session, transport],
    );

    // The read that starts the book, which is a request going out and therefore the one thing an effect is for. It runs
    // again when the tab changes or the credential does; nothing is cleared here, because what is held names the book
    // it was read for and a reading for another one is derived away below.
    useEffect(() => {
        void readFrom(null);
    }, [readFrom]);

    const standing = held.book === book && held.session === session ? held : nothingRead(book, session);

    return {
        contacts: standing.contacts,
        reading: standing.reading,
        paging: standing.paging,
        failure: standing.failure,
        complete: standing.complete,
        readMore: () => {
            if (standing.paging || standing.reading || standing.cursor === null) {
                return;
            }

            setHeld({ ...standing, paging: true, failure: null });
            void readFrom(standing.cursor);
        },
        readAgain: () => {
            setHeld({ ...standing, reading: true, failure: null });
            void readFrom(null);
        },
    };
}
