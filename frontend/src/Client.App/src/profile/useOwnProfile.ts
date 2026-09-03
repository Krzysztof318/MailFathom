// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    changeOwnDisplayName,
    readOwnDisplayName,
    type ClientSession,
    type MailFathomTransport,
    type PortraitImageType,
} from '@mailfathom/client-backend';
import type { PortraitExchange } from '../deployment/portraitExchange';

// Who the signed-in person is, as far as the client can draw them: the name this deployment records them under, the
// picture they chose, and whether this deployment would take a correction of the name. It is held here rather than in
// either screen that shows it because both do — the account menu draws it and the settings screen edits it — and two
// copies read separately would disagree the moment one of them wrote.
//
// It sits beside `preferences/useClientPreferences.ts` in shape and for the same reasons, including the one that is
// easy to miss: this hook outlives a sign-out, being mounted by the frame, so nothing it read for one credential may
// be drawn for the next. What answers that is deriving rather than clearing — state belonging to a session this no
// longer is stops being read instead of being corrected.

/** Who the client is drawing, and the three ways a person changes it. */
export interface OwnProfileInForce {
    /** What this deployment records them as called, or `null` while nothing has answered. */
    readonly displayName: string | null;

    /** Whether this deployment would take a correction of the name from this credential. */
    readonly changeable: boolean;

    /** Where their picture may be drawn from, or `null` where they have none or nothing has answered yet. */
    readonly picture: string | null;

    /** Whether the deployment refused the name last stated, which is a sentence about what was typed. */
    readonly nameNotAcceptable: boolean;

    /** Whether the last change of the name did not reach the deployment at all. */
    readonly nameNotStated: boolean;

    /** Whether the last change of the picture did not reach the deployment at all. */
    readonly pictureNotStated: boolean;

    readonly correctName: (displayName: string) => void;
    readonly choosePicture: (picture: Blob, type: PortraitImageType) => void;
    readonly removePicture: () => void;
}

// What is held, and whose it is. The session is carried beside it rather than trusted to have stayed the same, for the
// reason above.
interface HeldProfile {
    readonly session: ClientSession | null;
    readonly displayName: string | null;
    readonly changeable: boolean;
    readonly picture: string | null;
    readonly nameNotAcceptable: boolean;
    readonly nameNotStated: boolean;
    readonly pictureNotStated: boolean;
}

const heldForNobody: HeldProfile = {
    session: null,
    displayName: null,
    changeable: false,
    picture: null,
    nameNotAcceptable: false,
    nameNotStated: false,
    pictureNotStated: false,
};

/**
 * Holds who the signed-in person is, as this deployment records them.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — nobody signed in, no network,
 * or a credential this deployment does not let read — in which case nothing is read and the client draws nobody.
 * @param transport How a request carrying text reaches the deployment.
 * @param portraits How the picture is read and written, which is a second adapter because octets are not text.
 * @returns Who is drawn, and the three ways a person changes it.
 */
export function useOwnProfile(
    session: ClientSession | null,
    transport: MailFathomTransport,
    portraits: PortraitExchange,
): OwnProfileInForce {
    const [held, setHeld] = useState<HeldProfile>(heldForNobody);

    // The portrait read in flight, so that a screen which has moved on — or a replacement that has just been stored —
    // discards its answer. Abandoning travels on a controller rather than on a flag, for the reason
    // `shell/useConnection.ts` gives: it is asked through a function, so nothing decides at the first check what can
    // only become true at the second. The name read needs no ref beside it, because nothing re-issues one.
    const readingPicture = useRef<AbortController | null>(null);

    // Everything below reads through this rather than out of the state directly, which is what keeps one person's name
    // and picture off the next person's screen without a reset anywhere.
    const inForce = held.session === session ? held : heldForNobody;

    useEffect(() => {
        if (session === null) {
            return;
        }

        // Two controllers rather than one, because only one of the two reads is ever re-issued: replacing the picture
        // starts a second portrait read and has to abandon the first, and a single controller would abandon the name
        // read along with it — discarding an answer the upload had nothing to do with.
        const naming = new AbortController();
        const drawing = new AbortController();

        readingPicture.current = drawing;

        void (async () => {
            const answer = await readOwnDisplayName(session, transport);

            if (naming.signal.aborted || answer.outcome !== 'read') {
                return;
            }

            setHeld((current) => ({
                ...forSession(current, session),
                displayName: answer.value.displayName,
                changeable: answer.value.changeable,
            }));
        })();

        void (async () => {
            const answer = await portraits.read(session, drawing.signal);

            if (drawing.signal.aborted || answer.outcome === 'refused') {
                return;
            }

            setHeld((current) => ({
                ...forSession(current, session),
                picture: answer.outcome === 'drawn' ? answer.picture : null,
            }));
        })();

        return () => {
            naming.abort();

            // Whichever portrait read is current, which is this effect's own unless a replacement started a fresher
            // one — that one is what a screen going away has to abandon.
            readingPicture.current?.abort();
        };
    }, [session, transport, portraits]);

    // A picture that has just been replaced is read back rather than drawn from the file that was sent. The deployment
    // is what decides which octets are stored, and a screen showing the file instead would be showing something it
    // hoped had landed — which is the same defect as an optimistic write, worn as an image.
    //
    // Whatever read is still in flight is abandoned first, the way `preferences/useClientPreferences.ts` abandons its
    // own before every write. The read this replaces was started before the picture was: two answers to the same
    // question have no ordering between them, so an older one landing second would draw the picture the deployment
    // held before the upload — a defect that arrives looking like the upload having silently failed.
    function drawStoredPicture(asked: ClientSession): void {
        readingPicture.current?.abort();

        const attempted = new AbortController();
        readingPicture.current = attempted;

        void portraits.read(asked, attempted.signal).then((answer) => {
            if (attempted.signal.aborted || answer.outcome === 'refused') {
                return;
            }

            setHeld((current) =>
                current.session === asked
                    ? { ...current, picture: answer.outcome === 'drawn' ? answer.picture : null }
                    : current,
            );
        });
    }

    return {
        displayName: inForce.displayName,
        changeable: inForce.changeable,
        picture: inForce.picture,
        nameNotAcceptable: inForce.nameNotAcceptable,
        nameNotStated: inForce.nameNotStated,
        pictureNotStated: inForce.pictureNotStated,

        // What is on the screen changes because a person pressed something, so it changes in the handler rather than
        // in an effect watching what the handler set.
        correctName: (displayName) => {
            if (session === null) {
                return;
            }

            void changeOwnDisplayName(session, transport, displayName).then((answer) => {
                setHeld((current) => {
                    if (current.session !== session) {
                        return current;
                    }

                    // The name is taken from the answer rather than from what was typed, because it is trimmed on its
                    // way in and a screen redrawing what was typed would show something the deployment does not hold.
                    return {
                        ...current,
                        displayName: answer.outcome === 'recorded' ? answer.displayName : current.displayName,
                        nameNotAcceptable: answer.outcome === 'notAcceptable',
                        nameNotStated: answer.outcome === 'failed',
                    };
                });
            });
        },

        choosePicture: (picture, type) => {
            if (session === null) {
                return;
            }

            void portraits.replace(session, picture, type).then((answer) => {
                setHeld((current) =>
                    current.session === session
                        ? { ...current, pictureNotStated: answer.outcome !== 'stored' }
                        : current,
                );

                if (answer.outcome === 'stored') {
                    drawStoredPicture(session);
                }
            });
        },

        removePicture: () => {
            if (session === null) {
                return;
            }

            void portraits.remove(session).then((answer) => {
                setHeld((current) => {
                    if (current.session !== session) {
                        return current;
                    }

                    // Removal falls back to the initials, which the client already has from the name — so what is
                    // drawn afterwards needs nothing read back.
                    return {
                        ...current,
                        picture: answer.outcome === 'stored' ? null : current.picture,
                        pictureNotStated: answer.outcome !== 'stored',
                    };
                });
            });
        },
    };
}

/** What an answer is folded into: what is held where it belongs to this session, and nothing where it does not. */
function forSession(current: HeldProfile, session: ClientSession): HeldProfile {
    return current.session === session ? current : { ...heldForNobody, session };
}
