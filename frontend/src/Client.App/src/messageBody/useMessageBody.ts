// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    readCleanedMailBody,
    readMailBody,
    type CleanedMailBody,
    type ClientResult,
    type ClientSession,
    type MailBody,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { useMessageView } from '../preferences/messageView';

// Reading one message's body: the read itself, the reader's own ask for pictures from the sender, and which of the
// answers this client is holding may actually be drawn. What is done with it is `messageBody/Message.tsx`'s.
//
// **It is a hook rather than state inside that component because a surface has to be able to start this read before it
// has anything to draw it under.** The reading pane draws the body inside the branch that already holds the message's
// description, so a read owned by the component in that branch could not begin until the description had answered —
// two round trips one after the other for two answers that need nothing from each other. A caller starting the read
// where it mounts rather than where it draws is what makes the pair concurrent, and it is why the read is separable
// from the drawing at all.
//
// **Whether the read is wanted at all is the caller's to say**, and the two surfaces answer it for different reasons: a
// conversation of thirty messages holds thirty heads and one open body, so a collapsed message wants none of this, and
// the reading pane wants nothing while the machine has no network. What a read that is not wanted leaves standing is a
// body already drawn — nothing about it stopped being true — and never a failure, so wanting the read again is what
// recovers from one rather than a reader pressing through a refusal that belonged to a wait they have left.
//
// Asking for pictures re-reads that one message with the ask in the query, and nothing beneath this writes the answer
// down: leaving the message and coming back asks again, which is the whole of what ADR 0024 permits to be remembered.
//
// **Which of the three reading surfaces a message is drawn on is asked here rather than passed in**, because this is
// where the read is composed: the sender's own markup is a second thing the body route answers, so the setting is part
// of what is being asked for rather than something the drawing decides afterwards. That is also what keeps the
// representation off every other read — a client in the reduced view never asks for it.
//
// **The cleaned rendering is a second read rather than a third ask on the first**, and that is what makes the pane able
// to wait without ever being empty: the reduced document arrives from the body route and is drawable while the model is
// still deciding what of it to keep. It carries the same ask for the sender's pictures the body read carries, because a
// cleaning composed out of a different read would disagree with what is on the screen about what the message fetched.
// Nothing here writes the answer down, so leaving the message and coming back asks the deployment to derive it again.

/** What is being read: which message, under which asks, and which attempt at it. A change to any of them may read. */
interface Read {
    readonly storedEmailId: string;
    readonly remotePictures: boolean;

    /** Whether this read asks for the sender's own markup, which only the embedded view does. */
    readonly markup: boolean;

    readonly attempt: number;
}

interface Answered {
    readonly read: Read;
    readonly result: ClientResult<MailBody>;
}

/**
 * What a cleaning was derived for: which message, under which ask for the sender's pictures, and which attempt at it.
 *
 * It carries no markup, because the cleaning is the reduced document with blocks dropped and a reader in the embedded
 * view is being shown the sender's own markup instead — there is nothing for a cleaning to stand in front of there.
 */
interface Cleaning {
    readonly storedEmailId: string;
    readonly remotePictures: boolean;
    readonly attempt: number;
}

interface Derived {
    readonly ask: Cleaning;
    readonly result: ClientResult<CleanedMailBody>;
}

/** One message's body as this client is holding it: what may be drawn, what is still being read, and the ways on. */
export interface MessageBodyRead {
    /** The answer that may be drawn now, or `null` while nothing this hook holds may be. */
    readonly drawn: ClientResult<MailBody> | null;

    /** Whether a read is in flight, which is a different question from whether anything may be drawn. */
    readonly reading: boolean;

    /** Whether the read in flight is the reader's own ask for the sender's pictures, which has a surface reporting it. */
    readonly askingForPictures: boolean;

    /** Whether the current ask carries the sender's pictures, which is what a failed one offers a way back from. */
    readonly askedForPictures: boolean;

    /** Whether what is drawn may be shown as the sender's own markup: the view in force, and the ask it was read under. */
    readonly embeddedHtml: boolean;

    /**
     * The cleaning the deployment derived for what is drawn, or `null` where the view in force asks for none.
     *
     * A failure is a value here as everywhere: a derivation that did not arrive is a sentence over the reduced document
     * rather than a pane with nothing in it, so the caller draws the reason and the document it already has.
     */
    readonly cleaned: ClientResult<CleanedMailBody> | null;

    /** Whether a cleaning is being derived now, which the pane says over the reduced document while it waits. */
    readonly cleaning: boolean;

    readonly readAgain: () => void;
    readonly showRemotePictures: () => void;
    readonly showWithoutRemotePictures: () => void;
}

/** What a message is opened under, which is nothing it inherits from the message read before it. */
function opening(storedEmailId: string, markup: boolean): Read {
    return { storedEmailId, remotePictures: false, markup, attempt: 0 };
}

/**
 * The body a caller already holds, dressed as the answer to the read that would otherwise have fetched it.
 *
 * A conversation arrives with every message's body in it, so a surface drawing one of those messages has the answer
 * before this hook is called and a read of its own would be the request that answer exists to spare. It stands as the
 * opening read's answer and no more than that: it carries no markup and no remote picture, so a reader who asks for
 * either is read for exactly as they would have been.
 */
function carriedAs(storedEmailId: string, carried: MailBody | null): Answered | null {
    return carried === null
        ? null
        : { read: opening(storedEmailId, false), result: { outcome: 'read', value: carried } };
}

/**
 * The answer a read may still be drawn under, which is not every answer this hook happens to be holding.
 *
 * It has to be this message's, and it has to have asked for no more than the current read does: a caller that leaves a
 * message whose pictures were asked for and comes back before the next read answers would otherwise redraw the remote
 * sources under a visit where nobody asked, and tell the sender the message was opened again. The other direction is
 * kept deliberately — a message read without the pictures stays on the screen while the ask for them is in flight.
 */
function drawableUnder(answer: Answered | null, read: Read): Answered | null {
    if (answer?.read.storedEmailId !== read.storedEmailId) {
        return null;
    }

    return answer.read.remotePictures && !read.remotePictures ? null : answer;
}

/**
 * Whether the answer in hand already covers what the current read asks for, and there is therefore nothing to fetch.
 *
 * The markup is the one ask an older answer may satisfy in one direction only: an answer read with it carries the
 * reduced tree as well, so changing the view back draws what is already here, while an answer read without it has
 * nothing for the embedded view to draw and has to be read again. That is what makes changing the view twice cost one
 * read rather than two, and it is why the read carries the ask rather than the setting deciding after the fact.
 */
function covers(had: Read | undefined, wants: Read): boolean {
    return (
        had?.storedEmailId === wants.storedEmailId &&
        had.attempt === wants.attempt &&
        had.remotePictures === wants.remotePictures &&
        (had.markup || !wants.markup)
    );
}

/**
 * Reads one message's body, where the surface asking for it wants it read.
 *
 * Called where the surface mounts rather than where the body is drawn, so that the read starts at the moment the
 * message was opened rather than at the moment something else has answered.
 *
 * @param wanted Whether a read may be made at all: this message is the one being shown, and the deployment is reachable.
 * @param carried The body the caller already holds, which a conversation hands every message of it, or `null`.
 */
export function useMessageBody(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
    wanted: boolean,
    carried: MailBody | null = null,
): MessageBodyRead {
    const view = useMessageView();
    const embeddedHtml = view === 'embeddedHtml';
    const [read, setRead] = useState<Read>(() => opening(storedEmailId, embeddedHtml));

    // The answer carries the read it came from, so whether one is still in flight is computed rather than kept beside
    // it: two pieces of state that must agree is one piece of state and a function, and the answer to a previous read
    // is never drawn under the current one.
    const [answer, setAnswer] = useState<Answered | null>(() => carriedAs(storedEmailId, carried));

    // The ask belongs to the one message it was made for, so a message changing under this hook is not state to carry
    // over — the next message would otherwise be read with `remoteImages=true` although nobody asked for its pictures,
    // telling its sender it was opened. React's answer to a prop that invalidates state is to adjust it during render
    // rather than in an effect, which is why this is an assignment and not a second read.
    if (read.storedEmailId !== storedEmailId) {
        setRead(opening(storedEmailId, embeddedHtml));

        // The body handed in belongs to the message handed in with it, so the next message arrives with its own or
        // with none — never with the previous message's answer still standing as what may be drawn.
        setAnswer(carriedAs(storedEmailId, carried));
    } else if (read.markup !== embeddedHtml) {
        // The view changed under a message already on the screen. That is a changed ask rather than a changed message,
        // so the pictures and the attempt stay where they are — and the read below is skipped entirely where what is
        // held already carries what the new view draws.
        setRead({ ...read, markup: embeddedHtml });
    }

    // A refusal that stood while the read was wanted has nothing left to say once it is not, so it goes with the wait
    // rather than standing in front of whoever comes back to the message. Adjusted during render for the reason the
    // assignment above is, and it settles in one pass: what it clears is what its own condition reads.
    if (!wanted && answer?.result.outcome === 'failed') {
        setAnswer(null);
    }

    // What still has to be read, or `null` where no read is wanted and where the answer in hand already covers it.
    // Written as the effect's own dependency rather than as a guard inside it, so that an answer arriving is what
    // stops the next read rather than a condition evaluated over state the effect would have to depend on to see.
    const outstanding = !wanted || covers(answer?.read, read) ? null : read;

    useEffect(() => {
        if (outstanding === null) {
            return;
        }

        let listening = true;

        const ask = { remoteImages: outstanding.remotePictures, fullHtml: outstanding.markup };

        void readMailBody(session, transport, outstanding.storedEmailId, ask).then((answered) => {
            if (listening) {
                setAnswer({ read: outstanding, result: answered });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, outstanding]);

    // The cleaning asked for, whose answer is held beside the body's rather than inside it: the two are separate reads
    // of the same message and either may be in flight while the other is drawn, which is what lets the pane draw the
    // reduced document while the model is still deciding what of it to keep.
    const [derived, setDerived] = useState<Derived | null>(null);

    const wantsCleaning = wanted && view === 'cleaned';

    // Whether what is held answers the ask in force. Read off the three things the derivation depends on rather than
    // off an object built per render, so that the effect below depends on values rather than on an identity.
    const settled =
        derived !== null &&
        derived.ask.storedEmailId === storedEmailId &&
        derived.ask.remotePictures === read.remotePictures &&
        derived.ask.attempt === read.attempt;

    // Whether a derivation still has to be asked for, and what it would be asked under. The effect depends on the three
    // values rather than on an object built out of them, which the body's own read can afford because what it asks for
    // *is* its state: an ask composed per render would be a new dependency on every render and would ask forever.
    const deriving = wantsCleaning && !settled;
    const derivingPictures = read.remotePictures;
    const derivingAttempt = read.attempt;

    useEffect(() => {
        if (!deriving) {
            return;
        }

        let listening = true;
        const ask: Cleaning = { storedEmailId, remotePictures: derivingPictures, attempt: derivingAttempt };

        void readCleanedMailBody(session, transport, storedEmailId, derivingPictures).then((answered) => {
            if (listening) {
                setDerived({ ask, result: answered });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, storedEmailId, derivingPictures, derivingAttempt, deriving]);

    const held = drawableUnder(answer, read);

    return {
        drawn: held?.result ?? null,
        reading: outstanding !== null,

        // Which read is in flight, rather than whether one is. Only the ask for the sender's pictures has a surface
        // that reports it — the button somebody pressed, with the wait beneath it — so a read begun by anything else,
        // and changing the view over an open message is one, would otherwise put "Loading them…" under a button
        // nobody touched. What separates the two is that the answer being drawn was read without the pictures.
        askingForPictures: outstanding !== null && outstanding.remotePictures && !held?.read.remotePictures,

        askedForPictures: read.remotePictures,

        // Held to the same two halves the markup is: the view has to be the one in force, and the answer has to be the
        // one derived for the ask on the screen. A cleaning derived for an earlier ask is no more drawable than a
        // document read under one.
        cleaned: wantsCleaning && settled ? derived.result : null,
        cleaning: deriving,

        // Both halves rather than the setting alone: the view has to be the one in force *and* the answer on the
        // screen has to be one that was read under it, so a message drawn from an earlier answer stays the reduced
        // tree until the representation arrives instead of reporting markup nobody fetched.
        embeddedHtml: read.markup && (held?.read.markup ?? false),

        readAgain: () => {
            setRead((current) => ({ ...current, attempt: current.attempt + 1 }));
        },

        showRemotePictures: () => {
            setRead((current) => ({ ...current, remotePictures: true }));
        },

        // A failed ask for the sender's pictures has a way out that is not reloading the page: the message read
        // without them is one this deployment already answered with.
        showWithoutRemotePictures: () => {
            setRead((current) => ({ ...current, remotePictures: false, attempt: current.attempt + 1 }));
        },
    };
}
