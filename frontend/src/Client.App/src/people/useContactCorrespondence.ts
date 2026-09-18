// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    readContactCorrespondence,
    type ClientFailureReason,
    type ClientSession,
    type ContactCorrespondence,
    type MailFathomTransport,
} from '@mailfathom/client-backend';

// What the mail this credential may read already holds about the contact somebody has opened: the conversations their
// addresses appear in, and the documents they sent. It is read here rather than with the record itself because the two
// are separate routes and cost different things — the record is a row, and this is a correlation computed over the mail
// index on every read — which is exactly why the two columns it feeds draw their own wait rather than holding up the
// person's page.
//
// It is keyed on the contact and the credential for the reason `contactBook/useContactBook.ts` holds its own key: an
// answer for the person a reader has just left must never be drawn under the person they have just opened.

/** What one contact's correspondence says, and the way out of a read that did not answer. */
export interface ContactCorrespondenceInForce {
    /** What the correlation found, or `null` while nothing has answered. */
    readonly correspondence: ContactCorrespondence | null;

    readonly reading: boolean;

    /** Why the correlation did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    readonly readAgain: () => void;
}

// What is held, beside the contact and the credential it belongs to.
interface HeldCorrespondence {
    readonly contactId: string | null;
    readonly session: ClientSession | null;
    readonly correspondence: ContactCorrespondence | null;
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
}

function nothingRead(contactId: string | null, session: ClientSession | null): HeldCorrespondence {
    return {
        contactId,
        session,
        correspondence: null,
        reading: contactId !== null && session !== null,
        failure: null,
    };
}

/**
 * Holds what this deployment's mail says about one contact.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with.
 * @param transport How a request reaches the deployment.
 * @param contactId The contact opened, or `null` where none is.
 * @returns What the correlation said, and the way out of a read that did not answer.
 */
export function useContactCorrespondence(
    session: ClientSession | null,
    transport: MailFathomTransport,
    contactId: string | null,
): ContactCorrespondenceInForce {
    const [held, setHeld] = useState<HeldCorrespondence>(() => nothingRead(contactId, session));
    const latest = useRef(0);

    const read = useCallback(async (): Promise<void> => {
        if (session === null || contactId === null) {
            return;
        }

        const asked = ++latest.current;
        const answer = await readContactCorrespondence(session, transport, contactId);

        if (asked !== latest.current) {
            return;
        }

        setHeld({
            contactId,
            session,
            correspondence: answer.outcome === 'read' ? answer.value : null,
            reading: false,
            failure: answer.outcome === 'failed' ? answer.failure.reason : null,
        });
    }, [contactId, session, transport]);

    useEffect(() => {
        void read();
    }, [read]);

    const standing = held.contactId === contactId && held.session === session ? held : nothingRead(contactId, session);

    return {
        correspondence: standing.correspondence,
        reading: standing.reading,
        failure: standing.failure,
        readAgain: () => {
            setHeld({ ...standing, reading: true, failure: null });
            void read();
        },
    };
}
