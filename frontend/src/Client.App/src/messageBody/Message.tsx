// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import type { ClientFailureReason } from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { MessageBody } from './MessageBody';
import type { MessageBodyRead } from './useMessageBody';

// One message's body, drawn. Which message that is, and everything a surface lays out around it, is that surface's;
// the read itself is `messageBody/useMessageBody.ts`'s, started where the surface mounted rather than here. What this
// owns is the five states a surface that waits owes somebody, and the width all of it is read at.
//
// **The read is handed in rather than made here**, which is what lets the reading pane start it at the moment somebody
// opened the message rather than at the moment its description answered: this component is drawn inside the branch
// that already holds that description, so a read owned here would be a second round trip waiting on the first.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

interface MessageToDraw {
    /** The message's body as the surface is holding it, which is what this component draws the states of. */
    readonly body: MessageBodyRead;

    /** Which message that read belongs to, which is what makes the words on the screen one message's rather than any. */
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
    readonly onBodyDrawn: () => void;
}

// The measure a message is read at is the surface's rather than this component's, which is why nothing here writes
// one. The two surfaces answer it differently and both answers are the design project's: the reading pane binds one
// message's content and ranges it left against the list it was opened from, and a conversation binds a whole message —
// head and words together — and centres the column in the pane. A ceiling stated here would have made the second of
// those unreachable, since a ceiling inside a narrower one is the narrower one.
export function Message({ body, storedEmailId, quotedHistoryOnRequest = false, onBodyDrawn }: MessageToDraw) {
    const { translate } = useLocalization();

    // Which message's words are actually on the screen, which is the whole of what opening one means here — `null`
    // while a read is in flight and for a read that failed, because neither put anything in front of anybody.
    const drawn = body.drawn?.outcome === 'read' ? storedEmailId : null;

    // The message this component has already reported as drawn. A ref rather than a flag in state because it has to
    // survive `StrictMode` invoking the effect below twice on mount, for the reason the reading pane's own focus guard
    // is one — and because saying it twice would be this client reporting a message opened that nobody opened again.
    const reported = useRef<string | null>(null);

    useEffect(() => {
        if (drawn !== null && reported.current !== drawn) {
            reported.current = drawn;
            onBodyDrawn();
        }
    }, [drawn, onBodyDrawn]);

    // A message already drawn stays on the screen while a re-read runs, because replacing it with one line drops the
    // focus of whoever clicked and moves everything below their cursor on an interaction that changes no words. A
    // failure has nothing worth keeping, so a read started from one says it started.
    if (body.drawn === null || (body.reading && body.drawn.outcome === 'failed')) {
        return <p className="text-sm text-muted">{translate('body.reading')}</p>;
    }

    if (body.drawn.outcome === 'failed') {
        const failure = body.drawn.failure;

        return (
            <div className="flex flex-col items-start gap-2">
                <p className="text-sm text-warning">
                    {translate('body.failed', { reason: translate(failureLabels[failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
                {failure.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={body.readAgain} />
                ) : null}

                {/* A failed ask for the sender's pictures has a second way out, which is not reloading the page: the
                    message read without them is one this deployment already answered with. */}
                {body.askedForPictures ? (
                    <SecondaryButton
                        label={translate('body.showWithoutRemotePictures')}
                        onActivate={body.showWithoutRemotePictures}
                    />
                ) : null}
            </div>
        );
    }

    return (
        <MessageBody
            body={body.drawn.value}
            asking={body.askingForPictures}
            embeddedHtml={body.embeddedHtml}
            quotedHistoryOnRequest={quotedHistoryOnRequest}
            onShowRemotePictures={body.showRemotePictures}
        />
    );
}
