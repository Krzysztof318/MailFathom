// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    readMailBody,
    readMailMessage,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailBody,
    type MailBodyTruncation,
    type MailFathomTransport,
    type MailMessage,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { MessageMarkupFrame } from '../messageBody/MessageMarkupFrame';

// The second surface ADR 0024 takes: what the sender actually sent, drawn away from the reading pane for the reader
// whose message reduced badly. It is a surface rather than a dialog — the content area where a message is read, or a
// tab of its own where somebody works in tabs — and what it composes is the head the design project draws, the frame
// beneath it, and the footer that says what is actually holding each promise.
//
// **The footer states two guarantees and attributes each to what holds it**, per #1483. The frame is what stops the
// markup running. The representation is what stops it reporting: no sandboxing flag governs what a framed document
// fetches, so the addresses are gone before the markup reaches this client rather than refused once it is here. A
// footer that credited the frame with both would be reassurance the platform does not keep, on the one surface a
// careful reader opens precisely because they do not trust the sender.
//
// Two reads stand behind it, as they do in the reading pane and for the same reason: the head is what the message
// route answers and the markup is what the body route answers, so the head is drawn as soon as the first arrives.
// Asking for the sender's pictures re-reads the body alone, on the terms the pane already uses — per message, nothing
// durable, and gone again the moment the surface is closed.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

// What a reader is told about each bound that could have cut this representation short, and nothing where none did.
// A picture the sender attached that was left out is worth saying on this surface above all others, because a picture
// that is absent and a picture that was never there look identical: the surface exists to check the reduction, so a
// second silent loss on it would defeat the point of opening it.
const truncationNotes: Readonly<Record<MailBodyTruncation, MessageKey | null>> = {
    None: null,
    BodyCharacterLimit: 'fullHtml.truncated',
    ReadCharacterBudget: 'fullHtml.truncated',
    SensitiveContentScanCeiling: 'fullHtml.truncated',
    InlineImageOctetLimit: 'fullHtml.picturesTruncated',
};

/** What is being read: which message, under which ask, and which attempt at it. A change to any of the three reads. */
interface Read {
    readonly storedEmailId: string;
    readonly remotePictures: boolean;
    readonly attempt: number;
}

export function FullHtmlSurface({
    session,
    transport,
    storedEmailId,
    onClose,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The message whose own markup is being shown, which the reader confirmed one press ago. */
    readonly storedEmailId: string;

    /** Leaves the surface, which returns to whatever the reading column was drawing before it. */
    readonly onClose: () => void;
}) {
    const { locale, translate } = useLocalization();
    const [read, setRead] = useState<Read>({ storedEmailId, remotePictures: false, attempt: 0 });
    const [described, setDescribed] = useState<ClientResult<MailMessage> | null>(null);
    const [answer, setAnswer] = useState<{ read: Read; result: ClientResult<MailBody> } | null>(null);

    // Opening the surface is a view change, so focus goes to the start of it rather than staying on the control that
    // was pressed — which the platform has just put focus back on as the confirmation closed, and which is on a screen
    // the reader has now left. A ref rather than a flag, for the reason the reading pane's own guard is one: it has to
    // survive StrictMode invoking the effect twice.
    const head = useRef<HTMLElement>(null);
    const focusedOn = useRef<string | null>(null);

    useEffect(() => {
        if (focusedOn.current !== storedEmailId) {
            focusedOn.current = storedEmailId;
            head.current?.focus();
        }
    }, [storedEmailId]);

    useEffect(() => {
        let listening = true;

        void readMailMessage(session, transport, storedEmailId).then((answered) => {
            if (listening) {
                setDescribed(answered);
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, storedEmailId]);

    useEffect(() => {
        let listening = true;
        const ask = { remoteImages: read.remotePictures, fullHtml: true };

        void readMailBody(session, transport, read.storedEmailId, ask).then((answered) => {
            if (listening) {
                setAnswer({ read, result: answered });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, read]);

    const message = described?.outcome === 'read' ? described.value : null;
    const author = message?.headers.participants.find((participant) => participant.role === 'From') ?? null;
    const sentAt = message === null ? null : wordInstant(message.headers.sentAt, locale, 'full');

    return (
        <section ref={head} tabIndex={-1} aria-label={translate('fullHtml.surface')} className="flex h-full flex-col">
            <header className="flex flex-wrap items-center gap-3 border-b border-line px-4 py-2.5">
                <span className="rounded-xs bg-accent px-1.75 py-0.75 text-xs tracking-widest text-on-accent">
                    {translate('fullHtml.mark')}
                </span>

                <div className="flex min-w-0 flex-1 basis-48 flex-col">
                    <span className="truncate text-md font-semibold">
                        {message === null
                            ? translate('fullHtml.reading')
                            : (message.headers.subject ?? translate('message.noSubject'))}
                    </span>

                    <span className="truncate text-sm text-muted">
                        {translate('fullHtml.sentBy', {
                            author: author?.displayName ?? author?.address ?? translate('message.noAuthor'),
                            when: sentAt ?? translate('message.sentAtUnknown'),
                        })}
                    </span>
                </div>

                <button
                    type="button"
                    aria-label={translate('fullHtml.close')}
                    title={translate('fullHtml.close')}
                    className="flex size-8 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                    onClick={onClose}
                >
                    <Icon name="close" className="size-5" />
                </button>
            </header>

            <Markup
                answered={answer?.read === read ? answer.result : null}
                onRetry={() => {
                    setRead({ ...read, attempt: read.attempt + 1 });
                }}
            />

            <Isolation
                remotePictures={read.remotePictures}
                onShowRemotePictures={() => {
                    setRead({ ...read, remotePictures: true });
                }}
            />
        </section>
    );
}

// The markup itself, in the five states a surface that waits owes somebody. A body that answered without the
// representation is the one state a frame cannot express, so it is said in words: the deployment sent no markup for
// this message, which is what a message with no formatted part looks like from here.
function Markup({
    answered,
    onRetry,
}: {
    readonly answered: ClientResult<MailBody> | null;
    readonly onRetry: () => void;
}) {
    const { translate } = useLocalization();

    if (answered === null) {
        return (
            <p className="px-4 py-3 text-sm text-muted" role="status">
                {translate('fullHtml.reading')}
            </p>
        );
    }

    if (answered.outcome === 'failed') {
        return (
            <div className="flex flex-col items-start gap-2 px-4 py-3">
                <p className="text-sm text-warning" role="alert">
                    {translate('fullHtml.failed', { reason: translate(failureLabels[answered.failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
                {answered.failure.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={onRetry} />
                ) : null}
            </div>
        );
    }

    const markup = answered.value.selfContainedHtml;

    if (markup === null) {
        return (
            <p className="px-4 py-3 text-sm text-muted" role="status">
                {translate('fullHtml.noMarkup')}
            </p>
        );
    }

    const cut = truncationNotes[markup.truncation];

    return (
        <>
            <MessageMarkupFrame markup={markup.text} />

            {cut === null ? null : (
                <p className="border-t border-line-soft px-4 py-2 text-sm text-muted">{translate(cut)}</p>
            )}
        </>
    );
}

// The footer, which is the whole reason the two mechanisms are named separately anywhere. The first sentence belongs to
// the frame and the second to the representation, and the second changes once the reader has asked for the sender's
// pictures: from *nothing here reaches them* to *this one message is fetching from them, and nothing remembers that*.
function Isolation({
    remotePictures,
    onShowRemotePictures,
}: {
    readonly remotePictures: boolean;
    readonly onShowRemotePictures: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <footer className="flex flex-wrap items-center gap-x-3 gap-y-1.5 border-t border-line bg-sunken px-4 py-2.5 text-sm text-muted">
            <Icon name="lock" className="size-4 shrink-0" />

            <p className="min-w-0 flex-1 basis-64">
                {translate('fullHtml.cannotRun')}{' '}
                {translate(remotePictures ? 'fullHtml.picturesAsked' : 'fullHtml.reachesNobody')}
            </p>

            {remotePictures ? null : (
                <SecondaryButton label={translate('body.showRemotePictures')} onActivate={onShowRemotePictures} />
            )}
        </footer>
    );
}
