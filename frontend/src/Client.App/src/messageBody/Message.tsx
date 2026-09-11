// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import type { ClientFailureReason } from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import { Skeleton, SkeletonLines, type SkeletonLine } from '../controls/Skeleton';
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

// The lines a message's words stand as before they arrive, which is the design project's own raggedness: uneven
// lengths broken into two paragraphs, so the block reads as prose that is coming rather than as something loading.
const waitingLines: readonly SkeletonLine[] = [
    { fills: 97, height: 'h-2.5' },
    { fills: 92, height: 'h-2.5' },
    { fills: 99, height: 'h-2.5' },
    { fills: 68, height: 'h-2.5' },
    { fills: 0, height: 'h-1.5' },
    { fills: 95, height: 'h-2.5' },
    { fills: 88, height: 'h-2.5' },
    { fills: 44, height: 'h-2.5' },
];

/** A message's words before they have arrived, which two surfaces draw: the whole pane's wait, and the body's own. */
export function WordsWaiting() {
    return <SkeletonLines lines={waitingLines} className="gap-2.75 pt-1" />;
}

/**
 * A whole message before the deployment has answered, in the arrangement the design project draws: the subject and
 * the line under it, then the head that carries who wrote it, then the words.
 *
 * It stands where the message will rather than beside a sentence, so the answer lands into the space it was already
 * occupying. Three surfaces draw it and it sits here for the reason {@link WordsWaiting} does: the reading pane's
 * wait, the conversation's, and the state block the conversation carries are one shape stated once, and a second
 * arrangement of the same four blocks is how two screens come to wait differently for the same thing.
 *
 * The two lengths widen where the column is the whole window, which is the design project's own pair: a column beside
 * a list and a column that is the screen are two measures, and a skeleton drawn at the narrower one in the wider case
 * reads as a message that arrived half empty.
 */
export function MessageWaiting({ oneColumn }: { readonly oneColumn: boolean }) {
    return (
        <div className={`flex flex-col gap-3.5 ${oneColumn ? 'p-4' : 'px-6 py-5'}`}>
            <Skeleton className="h-3.75" fills={oneColumn ? 74 : 52} />
            <Skeleton className="h-2.5" fills={oneColumn ? 56 : 34} />

            <div className="flex items-center gap-2.75 border-t border-line-soft pt-4">
                <Skeleton className="size-7.5 shrink-0 rounded-full" />

                <div className="flex min-w-0 flex-1 flex-col gap-2">
                    <Skeleton className="h-3.75" fills={oneColumn ? 74 : 52} />
                    <Skeleton className="h-2.5" fills={oneColumn ? 56 : 34} />
                </div>
            </div>

            <WordsWaiting />
        </div>
    );
}

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
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
        return (
            <>
                {/* Said out of sight rather than not said: the lines below are what a reader looking at the column
                    sees, and this is the same statement for somebody who is not. */}
                <p className="sr-only" role="status">
                    {translate('body.reading')}
                </p>

                <WordsWaiting />
            </>
        );
    }

    if (body.drawn.outcome === 'failed') {
        const failure = body.drawn.failure;

        return (
            <div className="flex flex-col items-start gap-2">
                <p className="text-sm text-warning">
                    {translate('body.failed', { reason: translate(failureLabels[failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the five failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other four repeat identically on a second attempt. */}
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
