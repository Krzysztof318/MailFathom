// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    readMailBody,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailBody,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { MessageBody } from './MessageBody';

// One message, read and drawn. The pane that will choose which message and lay the space out around it is #1426's;
// what this owns is the read itself, the reader's own ask for pictures from the sender, and the five states a surface
// that waits owes somebody.
//
// Asking for pictures re-reads that one message with the ask in the query, and neither this component nor anything
// beneath it writes the answer down: leaving the message and coming back asks again, which is the whole of what
// ADR 0024 permits to be remembered.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

/** What is being read: which message, under which ask, and which attempt at it. A change to any of the three reads. */
interface Read {
    readonly storedEmailId: string;
    readonly remotePictures: boolean;
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

export function Message({
    session,
    transport,
    storedEmailId,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly storedEmailId: string;
}) {
    const { translate } = useLocalization();
    const [read, setRead] = useState<Read>({ storedEmailId, remotePictures: false, attempt: 0 });

    // The answer carries the read it came from, so whether one is still in flight is computed rather than kept beside
    // it: two pieces of state that must agree is one piece of state and a function, and the answer to a previous read
    // is never drawn under the current one.
    const [answer, setAnswer] = useState<Answered | null>(null);

    // The ask belongs to the one message it was made for, so a message changing under this component is not state to
    // carry over — the next message would otherwise be read with `remoteImages=true` although nobody asked for its
    // pictures, telling its sender it was opened. React's answer to a prop that invalidates state is to adjust it
    // during render rather than in an effect, which is why this is an assignment and not a second read.
    if (read.storedEmailId !== storedEmailId) {
        setRead({ storedEmailId, remotePictures: false, attempt: 0 });
    }

    useEffect(() => {
        let listening = true;

        void readMailBody(session, transport, read.storedEmailId, read.remotePictures).then((answered) => {
            if (listening) {
                setAnswer({ read, result: answered });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, read]);

    const held = drawableUnder(answer, read);
    const reading = held?.read !== read;

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
                    <button
                        className="rounded-md border border-line px-3 py-1 text-sm"
                        type="button"
                        onClick={() => {
                            setRead({ ...read, attempt: read.attempt + 1 });
                        }}
                    >
                        {translate('connection.retry')}
                    </button>
                ) : null}

                {/* A failed ask for the sender's pictures has a second way out, which is not reloading the page: the
                    message read without them is one this deployment already answered with. */}
                {read.remotePictures ? (
                    <button
                        className="rounded-md border border-line px-3 py-1 text-sm"
                        type="button"
                        onClick={() => {
                            setRead({ storedEmailId, remotePictures: false, attempt: read.attempt + 1 });
                        }}
                    >
                        {translate('body.showWithoutRemotePictures')}
                    </button>
                ) : null}
            </div>
        );
    }

    return (
        <MessageBody
            body={held.result.value}
            asking={reading}
            onShowRemotePictures={() => {
                setRead({ ...read, remotePictures: true });
            }}
        />
    );
}
