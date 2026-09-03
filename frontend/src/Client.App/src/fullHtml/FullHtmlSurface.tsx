// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useMemo, useRef, useState } from 'react';
import {
    readMailBody,
    readMailMessage,
    type ClientFailure,
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

/** The part of a `Read` the head is read under. The sender's pictures are a body ask and are not in it. */
type HeadRead = Pick<Read, 'storedEmailId' | 'attempt'>;

export function FullHtmlSurface({
    session,
    transport,
    storedEmailId,
    online,
    onClose,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The message whose own markup is being shown, which the reader confirmed one press ago. */
    readonly storedEmailId: string;

    /** Whether this machine has a network, which is a different thing from the deployment refusing to answer. */
    readonly online: boolean;

    /** Leaves the surface, which returns to whatever the reading column was drawing before it. */
    readonly onClose: () => void;
}) {
    const { locale, translate } = useLocalization();
    const [read, setRead] = useState<Read>({ storedEmailId, remotePictures: false, attempt: 0 });
    const [described, setDescribed] = useState<{ read: HeadRead; result: ClientResult<MailMessage> } | null>(null);
    const [answer, setAnswer] = useState<{ read: Read; result: ClientResult<MailBody> } | null>(null);
    const [connected, setConnected] = useState(online);

    // What the head is read under, held as one object so that both the effect below and the answer it keeps are keyed
    // on the same identity. Asking for the sender's pictures leaves that identity alone, which is what makes the ask a
    // second read of the body rather than of the whole message — and what stops the subject blanking while an identical
    // head request repeats.
    const headRead = useMemo<HeadRead>(
        () => ({ storedEmailId: read.storedEmailId, attempt: read.attempt }),
        [read.storedEmailId, read.attempt],
    );

    // Opening the surface is a view change, so focus goes to the start of it rather than staying on the control that
    // was pressed — which the platform has just put focus back on as the confirmation closed, and which is on a screen
    // the reader has now left. A ref rather than a flag, for the reason the reading pane's own guard is one: it has to
    // survive StrictMode invoking the effect twice.
    const region = useRef<HTMLElement>(null);
    const focusedOn = useRef<string | null>(null);

    useEffect(() => {
        if (focusedOn.current !== storedEmailId) {
            focusedOn.current = storedEmailId;
            region.current?.focus();
        }
    }, [storedEmailId]);

    // A failure the network gap itself caused goes with the gap, so what stands while there is no network is the
    // offline sentence rather than a refusal a reader would have to press through. Adjusted during render, which is
    // where React answers a changed prop, exactly as `readingPane/ReadingPane.tsx` answers the same prop.
    if (connected !== online) {
        setConnected(online);

        if (!online) {
            if (described?.result.outcome === 'failed') {
                setDescribed(null);
            }

            if (answer?.result.outcome === 'failed') {
                setAnswer(null);
            }
        }
    }

    // Both reads carry the same attempt, so one retry re-runs both and neither can be left behind on an older attempt
    // than the other. Nothing is read without a network, and coming back re-runs them — which is the whole of the
    // recovery from that direction and what makes the offline sentence's promise a true one.
    useEffect(() => {
        if (!online) {
            return;
        }

        let listening = true;

        void readMailMessage(session, transport, headRead.storedEmailId).then((answered) => {
            if (listening) {
                setDescribed({ read: headRead, result: answered });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, headRead, online]);

    useEffect(() => {
        if (!online) {
            return;
        }

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
    }, [session, transport, read, online]);

    const describing = described?.read === headRead ? described.result : null;
    const drawn = answer?.read === read ? answer.result : null;
    const message = describing?.outcome === 'read' ? describing.value : null;
    const author = message?.headers.participants.find((participant) => participant.role === 'From') ?? null;
    const sentAt = message === null ? null : wordInstant(message.headers.sentAt, locale, 'full');

    // **One failure state for two reads.** Either read failing leaves this surface unable to be what it is — the head
    // names the message and the body is the message — so a reader is told once, with one way out, rather than being
    // shown a frame under a header that never stops saying it is reading. The head's failure is reported first because
    // it is the one that would otherwise be silent: a frame drawn below it looks like a surface that worked.
    const failure =
        describing?.outcome === 'failed' ? describing.failure : drawn?.outcome === 'failed' ? drawn.failure : null;

    // The head names the message once it has been read, says it is reading while it is, and says nothing otherwise. A
    // head that failed is reported below, with the way out beside it; repeating it here in the present tense would have
    // the surface saying it is still reading something it has already given up on. With no network nothing is being
    // read at all, which is the same sentence for the same reason.
    const naming =
        message !== null
            ? (message.headers.subject ?? translate('message.noSubject'))
            : online && describing === null
              ? translate('fullHtml.reading')
              : null;

    return (
        <section ref={region} tabIndex={-1} aria-label={translate('fullHtml.surface')} className="flex h-full flex-col">
            <header className="flex flex-wrap items-center gap-3 border-b border-line px-4 py-2.5">
                <span className="rounded-xs bg-accent px-1.75 py-0.75 text-xs tracking-widest text-on-accent">
                    {translate('fullHtml.mark')}
                </span>

                <div className="flex min-w-0 flex-1 basis-48 flex-col">
                    {naming === null ? null : <span className="truncate text-md font-semibold">{naming}</span>}

                    {/* Nothing is claimed about who sent a message that has not been read. A line naming an unknown
                        author beside an unknown time is a sentence about this client's state dressed as a fact about
                        the message, on the surface a reader opened because they were checking. */}
                    {message === null ? null : (
                        <span className="truncate text-sm text-muted">
                            {translate('fullHtml.sentBy', {
                                author: author?.displayName ?? author?.address ?? translate('message.noAuthor'),
                                when: sentAt ?? translate('message.sentAtUnknown'),
                            })}
                        </span>
                    )}
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
                online={online}
                failure={failure}
                drawn={drawn}
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

// The markup itself, in the five states a surface that waits owes somebody. Two of them are said in words because no
// frame can express either: a body that answered without the representation, which is what a message with no formatted
// part looks like from here, and a machine with no network, which is a different thing from a deployment refusing.
//
// The failure it draws is either read's, resolved by the surface above rather than read off the body alone — a header
// stuck on *reading* above a frame that worked is the shape this exists to refuse.
function Markup({
    online,
    failure,
    drawn,
    onRetry,
}: {
    readonly online: boolean;
    readonly failure: ClientFailure | null;
    readonly drawn: ClientResult<MailBody> | null;
    readonly onRetry: () => void;
}) {
    const { translate } = useLocalization();

    // Offline is its own sentence rather than a failure worded politely, and it is said in place of everything else —
    // except a markup that already arrived. A drawn frame needs no network to go on showing what it shows, and nothing
    // about it stopped being true when the network went, which is exactly the guard `readingPane/ReadingPane.tsx` puts
    // on the same prop. What the sentence replaces is a surface with nothing on it yet.
    if (!online && drawn?.outcome !== 'read') {
        return (
            <p className="px-4 py-3 text-sm text-muted" role="status">
                {translate('message.offline')}
            </p>
        );
    }

    if (failure !== null) {
        return (
            <div className="flex flex-col items-start gap-2 px-4 py-3">
                <p className="text-sm text-warning" role="alert">
                    {translate('fullHtml.failed', { reason: translate(failureLabels[failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. It
                    re-runs both reads, because both are keyed on the one attempt. */}
                {failure.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={onRetry} />
                ) : null}
            </div>
        );
    }

    if (drawn?.outcome !== 'read') {
        return (
            <p className="px-4 py-3 text-sm text-muted" role="status">
                {translate('fullHtml.reading')}
            </p>
        );
    }

    const markup = drawn.value.selfContainedHtml;

    if (markup === null) {
        return (
            <p className="px-4 py-3 text-sm text-muted" role="status">
                {translate('fullHtml.noMarkup')}
            </p>
        );
    }

    const cut = truncationNotes[markup.truncation];

    // A representation that reduced to nothing is said in words, which is the obligation `MessageMarkupFrame` leaves to
    // this surface: it draws no frame for an empty document, because only the surface knows why the markup is absent.
    // A bound that cut it to nothing is what a reader is told where one did, and that the sender wrote none where none
    // did — an empty frame would be a white rectangle claiming neither.
    if (markup.text === '') {
        return (
            <p className="px-4 py-3 text-sm text-muted" role="status">
                {translate(cut ?? 'fullHtml.noMarkup')}
            </p>
        );
    }

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
