// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    readMailBody,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailBody,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useEmbeddedHtmlMessages } from '../preferences/messageView';
import { MessageBody } from './MessageBody';

// One message's body, read and drawn. Which message that is, and everything the reading pane lays out around it, is
// `readingPane/ReadingPane.tsx`'s; what this owns is the body read itself, the reader's own ask for pictures from the
// sender, the five states a surface that waits owes somebody, and the width all of it is read at.
//
// Asking for pictures re-reads that one message with the ask in the query, and neither this component nor anything
// beneath it writes the answer down: leaving the message and coming back asks again, which is the whole of what
// ADR 0024 permits to be remembered.
//
// **Which of the two reading surfaces a message is drawn on is asked here rather than passed in**, because this is
// where the read is composed: the sender's own markup is a second thing the body route answers, so the setting is part
// of what is being asked for rather than something the drawing decides afterwards. That is also what keeps the
// representation off every other read — a client in the reduced view never asks for it.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

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
 * The answer a read may still be drawn under, which is not every answer this component happens to be holding.
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

interface MessageToDraw {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly storedEmailId: string;

    /** Whether the conversation this message quoted is folded away until a reader asks for it, which a thread does. */
    readonly quotedHistoryOnRequest?: boolean;

    /**
     * Said once, when this message's words are on the screen, which is what opening a message means.
     *
     * The body having been drawn rather than a selection having moved, because a reading pane that follows the list
     * would otherwise report fifty messages opened for one press-and-hold of the arrow key: a read the reader scrolled
     * past is discarded rather than drawn, and a message whose body was never drawn was never opened. The round trip is
     * what a mail client with a preview pane otherwise needs a dwell timer for, and unlike a threshold it is not a
     * number anybody has to defend.
     *
     * Asking for the sender's pictures re-reads the same message and says nothing again, because the reader did not
     * open it twice.
     */
    readonly onBodyDrawn?: () => void;
}

// The measure a message is read at is the surface's rather than this component's, which is why nothing here writes
// one. The two surfaces answer it differently and both answers are the design project's: the reading pane binds one
// message's content and ranges it left against the list it was opened from, and a conversation binds a whole message —
// head and words together — and centres the column in the pane. A ceiling stated here would have made the second of
// those unreachable, since a ceiling inside a narrower one is the narrower one.
export function Message({
    session,
    transport,
    storedEmailId,
    quotedHistoryOnRequest = false,
    onBodyDrawn,
}: MessageToDraw) {
    const { translate } = useLocalization();
    const embeddedHtml = useEmbeddedHtmlMessages();
    const [read, setRead] = useState<Read>({ storedEmailId, remotePictures: false, markup: embeddedHtml, attempt: 0 });

    // The answer carries the read it came from, so whether one is still in flight is computed rather than kept beside
    // it: two pieces of state that must agree is one piece of state and a function, and the answer to a previous read
    // is never drawn under the current one.
    const [answer, setAnswer] = useState<Answered | null>(null);

    // The ask belongs to the one message it was made for, so a message changing under this component is not state to
    // carry over — the next message would otherwise be read with `remoteImages=true` although nobody asked for its
    // pictures, telling its sender it was opened. React's answer to a prop that invalidates state is to adjust it
    // during render rather than in an effect, which is why this is an assignment and not a second read.
    if (read.storedEmailId !== storedEmailId) {
        setRead({ storedEmailId, remotePictures: false, markup: embeddedHtml, attempt: 0 });
    } else if (read.markup !== embeddedHtml) {
        // The view changed under a message already on the screen. That is a changed ask rather than a changed message,
        // so the pictures and the attempt stay where they are — and the read below is skipped entirely where what is
        // held already carries what the new view draws.
        setRead({ ...read, markup: embeddedHtml });
    }

    // What still has to be read, or `null` where the answer in hand already covers it. Written as the effect's own
    // dependency rather than as a guard inside it, so that an answer arriving is what stops the next read rather than
    // a condition evaluated over state the effect would have to depend on to see.
    const outstanding = covers(answer?.read, read) ? null : read;

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

    const held = drawableUnder(answer, read);
    const reading = outstanding !== null;

    // Which message's words are actually on the screen, which is the whole of what opening one means here — `null`
    // while a read is in flight and for a read that failed, because neither put anything in front of anybody.
    const drawn = held?.result.outcome === 'read' ? held.read.storedEmailId : null;

    // The message this component has already reported as drawn. A ref rather than a flag in state because it has to
    // survive `StrictMode` invoking the effect below twice on mount, for the reason the reading pane's own focus guard
    // is one — and because saying it twice would be this client reporting a message opened that nobody opened again.
    const reported = useRef<string | null>(null);

    useEffect(() => {
        if (drawn !== null && reported.current !== drawn) {
            reported.current = drawn;
            onBodyDrawn?.();
        }
    }, [drawn, onBodyDrawn]);

    // A message already drawn stays on the screen while a re-read runs, because replacing it with one line drops the
    // focus of whoever clicked and moves everything below their cursor on an interaction that changes no words. A
    // failure has nothing worth keeping, so a read started from one says it started.
    if (held === null || (reading && held.result.outcome === 'failed')) {
        return <p className="text-sm text-muted">{translate('body.reading')}</p>;
    }

    if (held.result.outcome === 'failed') {
        return (
            <div className="flex flex-col items-start gap-2">
                <p className="text-sm text-warning">
                    {translate('body.failed', { reason: translate(failureLabels[held.result.failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
                {held.result.failure.reason === 'unavailable' ? (
                    <SecondaryButton
                        label={translate('connection.retry')}
                        onActivate={() => {
                            setRead({ ...read, attempt: read.attempt + 1 });
                        }}
                    />
                ) : null}

                {/* A failed ask for the sender's pictures has a second way out, which is not reloading the page: the
                    message read without them is one this deployment already answered with. */}
                {read.remotePictures ? (
                    <SecondaryButton
                        label={translate('body.showWithoutRemotePictures')}
                        onActivate={() => {
                            setRead({ ...read, remotePictures: false, attempt: read.attempt + 1 });
                        }}
                    />
                ) : null}
            </div>
        );
    }

    return (
        <MessageBody
            body={held.result.value}
            asking={reading}
            /* Both halves rather than the setting alone: the view has to be the one in force *and* the answer on the
               screen has to be one that was read under it, so a message drawn from an earlier answer stays the reduced
               tree until the representation arrives instead of reporting markup nobody fetched. */
            embeddedHtml={read.markup && held.read.markup}
            quotedHistoryOnRequest={quotedHistoryOnRequest}
            onShowRemotePictures={() => {
                setRead({ ...read, remotePictures: true });
            }}
        />
    );
}
