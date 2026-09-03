// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import { readMailAttachment, type ClientSession } from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import {
    showingFailureOf,
    useAttachmentExchange,
    type AttachmentRead,
    type ShowingRefusal,
} from '../deployment/attachmentExchange';
import type { OpenedAttachment } from '../workspace/openAttachment';
import { Downloading } from './Downloading';
import { useDownloadingAttachment } from './downloadingAttachment';
import { sizeOf } from '../localization/octets';
import { kindOf } from './fileKind';
import { shownAttachment, type NotShown } from './shownAttachment';

// One file a message carries, opened inside the client instead of saved. It stands where the message was — in the
// reading column, as the fourth kind of tab the design project draws — and closing it is a return to that message
// rather than a way out of the client.
//
// **What it shows is decided before anything is fetched**, by `shownAttachment.ts` beside it, which is where the two
// shapes this client draws and the reasoning behind each of them live. A file of any other kind, and a file of either
// kind larger than this surface draws, says so here and offers the download it has always had — which is a screen that
// answers rather than a control that does nothing.
//
// Nothing a file carries reaches a host other than the deployment. The octets arrive over the client surface under the
// credential the reader signed in with, and what is drawn from them is a picture in an `img` or words React escaped:
// neither resolves a reference, so a file whose content names an address cannot tell that address it was opened.

// What a refusal is worded as: one sentence each, saying what could not be done and what to do about it, exactly as the
// download beside it does — a reader who pressed *open* is owed as much as one who pressed *download*.
//
// `abandoned` is here for exhaustiveness rather than because a reader sees it: the only signal that abandons a read on
// this surface is the one this component owns, raised as the component goes away. An answer larger than the message
// described and octets nothing could be drawn from are the same sentence, because they are the same thing to a reader —
// what arrived is not the file, and the way on is to download it and say so.
const failureMessages: Readonly<Record<ShowingRefusal, MessageKey>> = {
    abandoned: 'attachment.notShownUnavailable',
    unauthenticated: 'attachment.notShownUnauthenticated',
    unauthorized: 'attachment.notShownUnauthorized',
    unavailable: 'attachment.notShownUnavailable',
    largerThanDescribed: 'attachment.notShownUnreadable',
    unreadable: 'attachment.notShownUnreadable',
};

const notShownMessages: Readonly<Record<NotShown, MessageKey>> = {
    kindNotShown: 'attachment.notShownKind',
    largerThanShown: 'attachment.notShownSize',
};

/**
 * What is being read, held as state rather than derived.
 *
 * The file cannot change under this surface — a different file is a different surface, keyed by the one it opened — so
 * what a request is composed from is settled at the mount, and trying again is a change to the attempt beside it.
 */
interface Reading {
    readonly drawnAs: ReturnType<typeof shownAttachment>;
    readonly attempt: number;
}

export function AttachmentView({
    session,
    opened,
    online,
    onClose,
}: {
    readonly session: ClientSession;

    /** The file being read, and the message it belongs to. */
    readonly opened: OpenedAttachment;

    readonly online: boolean;
    readonly onClose: () => void;
}) {
    const { locale, translate } = useLocalization();
    const { attachment, storedEmailId } = opened;
    const { download, start, stop } = useDownloadingAttachment(session, storedEmailId, attachment);

    const region = useRef<HTMLElement>(null);
    const named = attachment.fileName ?? translate('attachment.unnamed');

    // Opening a file is a view change, so focus goes to the start of it rather than staying on the row that opened it —
    // which is behind the file now, and for a keyboard reader is where reading silently stops. The region exists from
    // the first render, so focus is placed on the mount rather than waited for as the pane waits for a message.
    useEffect(() => {
        region.current?.focus();
    }, []);

    return (
        <section ref={region} tabIndex={-1} aria-label={named} className="flex min-h-full min-w-0 flex-col bg-sunken">
            <div className="flex shrink-0 flex-wrap items-center gap-2.5 border-b border-line bg-panel px-4 py-2.75">
                <span className="shrink-0 rounded-xs bg-accent px-1.75 py-0.75 text-xs font-semibold tracking-wide text-on-accent uppercase">
                    {kindOf(attachment.fileName, attachment.mediaType)}
                </span>

                <h2 className="min-w-0 truncate text-md font-semibold text-text">{named}</h2>
                <span className="shrink-0 text-sm text-faint">{sizeOf(attachment.sizeOctets, locale)}</span>

                <div className="ms-auto flex shrink-0 items-center gap-1">
                    <SurfaceControl
                        label={translate('attachment.download', { name: named })}
                        icon="download"
                        onActivate={start}
                    />

                    <SurfaceControl
                        label={translate('attachment.close', { name: named })}
                        icon="close"
                        onActivate={onClose}
                    />
                </div>
            </div>

            <div className="shrink-0 px-4 text-sm empty:hidden">
                <Downloading download={download} name={named} whole={attachment.sizeOctets} onStop={stop} />
            </div>

            <div className="flex min-h-0 flex-1 flex-col items-center overflow-auto px-5 py-6">
                <Inside session={session} opened={opened} online={online} named={named} />
            </div>
        </section>
    );
}

// What the body of the viewer holds, which is the file or the sentence standing in for it. It is a component of its own
// because it is what gains the read: the header above names a file whether or not one arrived, and a download offered
// there goes on running while a read below is tried again.
function Inside({
    session,
    opened,
    online,
    named,
}: {
    readonly session: ClientSession;
    readonly opened: OpenedAttachment;
    readonly online: boolean;
    readonly named: string;
}) {
    const { translate } = useLocalization();
    const exchange = useAttachmentExchange();
    const { attachment, storedEmailId } = opened;
    const [reading, setReading] = useState<Reading>(() => ({ drawnAs: shownAttachment(attachment), attempt: 0 }));
    const [answer, setAnswer] = useState<AttachmentRead | null>(null);

    // An answer belongs to the read that produced it, and losing the network ends that read: coming back starts
    // another, so the answer from before it would stand on the screen while the new one is still in flight — a surface
    // looking finished during a wait, which is what somebody acts on twice. Dropped during the render that observes
    // the change rather than in an effect, which is React's own way of letting go of state a prop has outlived.
    const [readWithNetwork, setReadWithNetwork] = useState(online);

    if (readWithNetwork !== online) {
        setReadWithNetwork(online);
        setAnswer(null);
    }

    // Nothing is read without a network, and coming back re-runs this — which is the whole of the recovery from that
    // direction, and what makes the offline sentence's promise that the file opens on its own a true one. Nothing is
    // read for a file this surface will not draw either, which is what lets that case say so with no octet fetched.
    useEffect(() => {
        const drawnAs = reading.drawnAs;

        if (typeof drawnAs === 'string' || !online) {
            return;
        }

        const abandoning = new AbortController();

        void readMailAttachment(
            session,
            storedEmailId,
            attachment.position,
            attachment.sizeOctets,
            (request) => exchange.read(request, drawnAs, abandoning.signal),
            showingFailureOf,
        ).then((read) => {
            if (!abandoning.signal.aborted) {
                setAnswer(read);
            }
        });

        return () => {
            abandoning.abort();
        };
    }, [session, exchange, storedEmailId, attachment, reading, online]);

    if (typeof reading.drawnAs === 'string') {
        return <Said message={notShownMessages[reading.drawnAs]} />;
    }

    if (!online) {
        return <Said message="attachment.offline" />;
    }

    if (answer === null) {
        return <Said message="attachment.reading" name={named} />;
    }

    if (answer.outcome === 'refused') {
        return (
            <div className="flex max-w-reading flex-col items-start gap-2">
                <p className="text-base text-warning" role="alert">
                    {translate(failureMessages[answer.refusal])}
                </p>

                {/* Reading again is the way out of exactly one of the refusals, for the reason
                    `shell/ConnectionSummary.tsx` gives: the others repeat identically on a second attempt. */}
                {answer.refusal === 'unavailable' ? (
                    <SecondaryButton
                        label={translate('connection.retry')}
                        onActivate={() => {
                            setReading({ drawnAs: reading.drawnAs, attempt: reading.attempt + 1 });
                        }}
                    />
                ) : null}
            </div>
        );
    }

    if (answer.content === '') {
        return <Said message="attachment.empty" />;
    }

    if (reading.drawnAs.as === 'picture') {
        // Named by the file rather than described, because nothing here has read what the picture shows: the sender's
        // own name for it is the only thing anybody can say about it truthfully.
        return <img alt={named} src={answer.content} className="max-w-full rounded-md bg-panel object-contain" />;
    }

    // `pre` because the file's own line breaks and spacing are what it holds, and wrapping so a long line reflows in
    // the column rather than making the pane scroll sideways past the message it was opened from.
    return (
        <pre className="w-full max-w-reading rounded-md bg-panel p-4 text-sm break-words whitespace-pre-wrap text-text">
            {answer.content}
        </pre>
    );
}

/** The one sentence shape this surface's body takes where there is no file on it to draw. */
function Said({ message, name }: { readonly message: MessageKey; readonly name?: string }) {
    const { translate } = useLocalization();

    return (
        <p className="max-w-reading text-base text-muted" role="status">
            {translate(message, name === undefined ? undefined : { name })}
        </p>
    );
}
