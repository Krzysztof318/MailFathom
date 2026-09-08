// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    readMailMessage,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailCarried,
    type MailFathomTransport,
    type MailMessage,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SenderAvatar } from '../controls/SenderAvatar';
import { ShowFullHtml } from '../fullHtml/ShowFullHtml';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useEmbeddedHtmlMessages } from '../preferences/messageView';
import { useReadMarking } from '../readMarking/useReadMarking';
import { useSignalledChanges } from '../signals/signalledChanges';
import { useOpenAttachment } from '../workspace/openAttachment';
import { useWorkspace } from '../workspace/useWorkspace';
import { Message } from '../messageBody/Message';
import { useMessageBody } from '../messageBody/useMessageBody';
import { BackToList } from '../mailSpace/BackToList';
import { NothingOpen } from '../mailSpace/NothingOpen';
import { useTwoPanes } from '../shell/useWideWorkspace';
import { Attachments } from './Attachments';
import { MessageHeaders } from './MessageHeaders';
import { sizeOf } from '../localization/octets';
import { SenderVerdict } from './SenderVerdict';

// Reading one message, which is the act everything else in this client exists to support and where the most is on screen
// at once. What this component owns is the composition and the honesty of it: the headers, what the deployment
// established about who actually sent the message, the body beneath them, and the files it carries — each drawn from
// what the service answered rather than from anything worked out here.
//
// Two reads stand behind it and that is deliberate rather than incidental. The description and the body are separately
// expensive, so the header block is drawn the moment the first answers and the body says it is still reading underneath
// it; that is this screen's partial state rather than a gap in it. **Both are started at the moment the message was
// opened**, neither waiting on the other to settle: the identity both of them need is what the reader pressed, so a
// body read owned by the component drawn inside the description's own branch would have spent a second round trip —
// and the derivation every request on this surface pays — for an answer that needed nothing from the first.
//
// Neither of those reads writes to a mailbox, and that is the property ADR 0007 bought as a property of the types: both
// are `GET`s against the local copy, and the route that serves a body holds no write session to reach a mail server
// with. What does mark the message read is a mutation of its own, authored by the person having opened it and submitted
// once the body is on the screen — ADR 0026 — so a defect in this pane cannot become a defect that writes to somebody's
// mailbox.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

// The most of a selected passage the workspace carries. A question is asked about a fragment somebody pointed at, so a
// select-all is a gesture rather than a scope, and a whole message in the workspace would travel into every later screen
// that reads it.
const longestFragment = 2000;

/** What is being read: which message, which attempt at it, and whether the attempt may show. A change to any reads again. */
interface Read {
    readonly storedEmailId: string;
    readonly attempt: number;

    /**
     * Whether what is already drawn stays on the screen while this attempt is in flight.
     *
     * A reader who pressed the retry control asked for the read and is owed the sentence saying it is happening. A
     * reader whose message the deployment said had changed asked for nothing, so replacing what they are reading with
     * *Reading your mail…* would take the message away from somebody who did not touch anything — which is the same
     * defect as moving a row under them.
     */
    readonly quietly: boolean;
}

interface Answered {
    readonly read: Read;
    readonly result: ClientResult<MailMessage>;
}

export function ReadingPane({
    session,
    transport,
    storedEmailId,
    online,
    onShowFullHtml,
    arriving = false,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly storedEmailId: string | null;
    readonly online: boolean;

    /**
     * Opens the surface drawing the sender's own markup for the message named, which the head's own control asks for.
     *
     * Handed in rather than reached for, because where that surface goes is the Mail space's decision — a tab of its
     * own beside everything else open, or in front of the message where a person does not work in tabs — and a pane
     * that decided it would be deciding something it cannot see.
     */
    readonly onShowFullHtml: (storedEmailId: string, subject: string | null) => void;

    /**
     * Whether the reader is arriving at this message rather than landing on it, which decides whether focus is placed.
     *
     * A pane mounts afresh both when a space opens on whatever was last read and when a conversation standing in front
     * of that message is closed. The first is a landing and the second is a navigation, and nothing in the mount itself
     * tells them apart — so what does is the one component that watched the conversation go.
     */
    readonly arriving?: boolean;
}) {
    return (
        <section className="flex min-h-full flex-col">
            {storedEmailId === null ? (
                <NothingOpen arriving={false} onReopenLastRead={null} />
            ) : (
                <OpenMessage
                    session={session}
                    transport={transport}
                    storedEmailId={storedEmailId}
                    online={online}
                    onShowFullHtml={onShowFullHtml}
                    arriving={arriving}
                />
            )}
        </section>
    );
}

// The message that is actually open, split out so that opening the first one mounts it rather than changing what an
// already-mounted component is reading: everything below holds state about one message, and none of it is the empty
// pane's.
function OpenMessage({
    session,
    transport,
    storedEmailId,
    online,
    onShowFullHtml,
    arriving,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly storedEmailId: string;
    readonly online: boolean;
    readonly onShowFullHtml: (storedEmailId: string, subject: string | null) => void;
    readonly arriving: boolean;
}) {
    const { locale, translate } = useLocalization();
    const twoPanes = useTwoPanes();
    const embeddedHtml = useEmbeddedHtmlMessages();
    const { workspace, revise } = useWorkspace();
    const openAttachment = useOpenAttachment();
    const { markRead } = useReadMarking();
    const signalledChanges = useSignalledChanges();
    const [read, setRead] = useState<Read>({ storedEmailId, attempt: 0, quietly: false });
    const [answer, setAnswer] = useState<Answered | null>(null);
    const [connected, setConnected] = useState(online);

    // The body's own read, started here rather than inside the component that draws it, which is what makes the pair
    // concurrent: this component mounts when the message is opened, and the branch drawing the body exists only once
    // the description has answered.
    const bodyRead = useMessageBody(session, transport, storedEmailId, online);

    // The opener is a new function on every render, so it is held rather than named as a dependency below: the effect
    // that follows a citation reacts to the citation and to the answer, and naming this would run it once per render.
    const openCitedFile = useRef(openAttachment);

    const opened = useRef<HTMLElement>(null);
    const body = useRef<HTMLDivElement>(null);
    // The message focus was last placed on, which starts as the one this pane mounted with so that landing on a message
    // does not steal focus. A reader arriving back from the conversation that stood in front of it is not landing, so
    // that mount starts having focused nothing and the effect below places it once the message is drawable.
    const focusedOn = useRef(arriving ? null : storedEmailId);

    useEffect(() => {
        openCitedFile.current = openAttachment;
    });

    // A message changing under this component invalidates what is being read, which React answers by adjusting state
    // during the render rather than in an effect that would draw the previous message's answer once first.
    if (read.storedEmailId !== storedEmailId) {
        setRead({ storedEmailId, attempt: 0, quietly: false });
    }

    // A failure the network gap itself caused goes with the gap, so what stands while there is no network is the
    // sentence below rather than a refusal to try again that a reader would have to press through. A message already
    // drawn stays where it is: nothing about it stopped being true, and it is the truest thing anybody has offline.
    // Adjusted during render, which is where React answers a changed prop, for the reason `folders/FolderTree.tsx`
    // gives about the frame this would otherwise be drawn one late in.
    if (connected !== online) {
        setConnected(online);

        if (!online && answer?.result.outcome === 'failed') {
            setAnswer(null);
        }
    }

    // Nothing is read without a network, and coming back re-runs this — which is the whole of the recovery from that
    // direction, and what makes the offline sentence's promise that the message opens on its own a true one.
    useEffect(() => {
        if (!online) {
            return;
        }

        let listening = true;

        void readMailMessage(session, transport, read.storedEmailId).then((answered) => {
            if (!listening) {
                return;
            }

            setAnswer({ read, result: answered });
        });

        return () => {
            listening = false;
        };
    }, [session, transport, read, online]);

    // A search result cited a file rather than the message, and a description of the message is what turns that
    // coordinate into something openable: the citation carries a position, and only the description says how large the
    // file at that position declares itself to be, which is the bound the download is read under.
    //
    // It waits for the description to be here rather than for a read to answer, because the cited message may be the
    // one already open: opening a message that is already the message opens nothing, so nothing re-reads and a citation
    // followed inside the read would never be followed at all. That is the everyday case — reading a message, searching,
    // and finding that the match is inside a file it carries.
    useEffect(() => {
        const cited = workspace.citedAttachment;

        // A description that failed keeps the citation, so the retry the reader is offered follows it rather than
        // dropping it. One that arrived spends it either way: a citation is one act half finished, and somebody who has
        // read another message since has abandoned it — leaving it set is how a message opened later opens a file
        // nobody asked for.
        if (cited === null || answer?.result.outcome !== 'read' || answer.read.storedEmailId !== storedEmailId) {
            return;
        }

        revise({ citedAttachment: null });

        if (cited.storedEmailId !== storedEmailId) {
            return;
        }

        const file = answer.result.value.attachments.find((held) => held.position === cited.position);

        if (file !== undefined) {
            openCitedFile.current({ storedEmailId: cited.storedEmailId, attachment: file });
        }
    }, [workspace.citedAttachment, answer, storedEmailId, revise]);

    // A message the deployment says has changed is read again where it is the one on the screen, quietly, so what a
    // reader is part-way through stays in front of them until the new answer replaces it. A signal naming other mail is
    // not this message's business: the list it names re-reads its own rows.
    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (signal.kind === 'mail.changed' && signal.emails.includes(storedEmailId)) {
                    setRead((current) => ({ storedEmailId, attempt: current.attempt + 1, quietly: true }));
                }
            }),
        [signalledChanges, storedEmailId],
    );

    // The fragment somebody selected belonged to the message they were reading, so it goes when the message does:
    // carrying it into the next one would scope a question to words that are no longer on the screen. It happens as the
    // message changes rather than once the next one has been read, because the words are already gone by then.
    useEffect(() => {
        revise({ fragment: null });
    }, [storedEmailId, revise]);

    // What a person selected becomes the scope the intent field asks its next question under. It is read from the
    // gesture that produced it rather than from an effect watching the document, and it is bounded, trimmed, and
    // confined to this message's own words: a selection that started outside the body is not part of the message.
    function capture(): void {
        const selected = window.getSelection();
        const region = body.current;

        if (selected === null || region === null) {
            return;
        }

        if (!region.contains(selected.anchorNode) || !region.contains(selected.focusNode)) {
            return;
        }

        const fragment = selected.toString().trim().slice(0, longestFragment);

        revise({ fragment: fragment === '' ? null : fragment });
    }

    // The answer to the attempt in flight, or — while a quiet attempt is in flight — the one already on the screen,
    // which is what keeps a signalled re-read from blanking a message its reader is part-way through.
    const held =
        answer?.read === read || (read.quietly && answer?.read.storedEmailId === read.storedEmailId) ? answer : null;
    const drawable = held?.result.outcome === 'read';

    // A message opening is a view change, so focus goes to the start of it rather than staying on whatever opened it —
    // which for a list is a row that is still on the screen and for a keyboard reader is where reading silently stops.
    // It waits for the message to be drawable, because the element focus is placed on does not exist while the read is
    // still in flight and a focus call before then would silently move nothing.
    //
    // Not for the message this pane opened with, for the reason `shell/Space.tsx` gives: landing on a message is not a
    // navigation, and a ref holding the message last focused survives StrictMode's second invocation where a flag would
    // not. Closing a conversation is the one mount that is a navigation, and `arriving` is how it says so.
    useEffect(() => {
        if (drawable && focusedOn.current !== storedEmailId) {
            focusedOn.current = storedEmailId;
            opened.current?.focus();
        }
    }, [drawable, storedEmailId]);

    // Offline is its own sentence rather than a failure worded politely, and it is said only where there is nothing to
    // draw instead: a message already on the screen is the truest thing anybody has, and the frame above already says
    // the machine has no network.
    // Before there is a head to carry the way back to the list, the states below carry it themselves in the
    // composition that draws one pane at a time — a reader waiting on a message, or told it could not be read, is
    // otherwise standing in a column with no way out of it.
    const wayBack = twoPanes ? null : (
        <div className="flex items-center px-2 pt-2">
            <BackToList />
        </div>
    );

    if (!online && held?.result.outcome !== 'read') {
        return (
            <>
                {wayBack}
                <p className="px-5.5 py-4 text-sm text-muted" role="status">
                    {translate('message.offline')}
                </p>
            </>
        );
    }

    if (held === null) {
        return (
            <>
                {wayBack}
                <p className="px-5.5 py-4 text-sm text-muted" role="status">
                    {translate('message.reading')}
                </p>
            </>
        );
    }

    if (held.result.outcome === 'failed') {
        return (
            <div className="flex flex-col items-start gap-2 px-5.5 py-4">
                {wayBack}
                <p className="text-sm text-warning" role="alert">
                    {translate('message.failed', { reason: translate(failureLabels[held.result.failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
                {held.result.failure.reason === 'unavailable' ? (
                    <SecondaryButton
                        label={translate('connection.retry')}
                        onActivate={() => {
                            setRead({ storedEmailId, attempt: read.attempt + 1, quietly: false });
                        }}
                    />
                ) : null}
            </div>
        );
    }

    const message = held.result.value;
    const threadId = message.threadId;
    const author = message.headers.participants.find((participant) => participant.role === 'From') ?? null;
    const numbers = new Intl.NumberFormat(locale);

    // Named by its own subject, which is what a reader arriving in the region needs to hear and what tells one message's
    // region from the body's inside it. The heading below says the same words on the screen; this is what the region
    // itself is called.
    return (
        <article
            ref={opened}
            tabIndex={-1}
            aria-label={message.headers.subject ?? translate('message.noSubject')}
            className="flex flex-col"
        >
            <MessageHeaders headers={message.headers} />

            <div className="flex flex-col gap-3 px-5.5 py-4.5">
                <SenderVerdict verdict={message.sender} />

                {/* The way into the conversation this message belongs to, offered where the service threaded it and
                    absent where it did not: a control that opened a conversation of one message would be a control
                    that answers nothing. It carries this message, so the conversation opens at what is being read
                    rather than at its beginning, and closing it returns here — the selection this pane draws from is
                    what it was opened from. Drawn as the pill the design project stands between a message and the
                    conversation behind it. */}
                {threadId === null ? null : (
                    <div className="flex justify-center">
                        <button
                            type="button"
                            className="rounded-full border border-line bg-sunken px-3.5 py-1.75 text-base text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                revise({ conversation: { threadId, openAt: storedEmailId } });
                            }}
                        >
                            {translate('thread.open')}
                        </button>
                    </div>
                )}

                {/* The message as the design project draws one in the reading column: flat on the column, at the
                    measure a conversation's messages take, with who wrote it and what the copy holds on its first
                    line — how many attachments, the sender's own markup where there is one, and when this deployment
                    recorded it — and what it says under that. */}
                <div className="mx-auto flex w-full max-w-conversation flex-col gap-3">
                    <div className="flex items-center gap-2.75">
                        <SenderAvatar
                            displayName={author?.displayName ?? null}
                            address={author?.address ?? null}
                            place="card"
                        />

                        <span className="min-w-0 truncate text-md font-semibold text-text">
                            {author === null ? translate('message.noAuthor') : (author.displayName ?? author.address)}
                        </span>

                        <span className="flex-1" />

                        {message.attachments.length === 0 ? null : (
                            <span className="flex shrink-0 items-center gap-0.75 text-xs text-faint">
                                <Icon name="attach_file" className="size-3.5" />
                                {numbers.format(message.attachments.length)}
                            </span>
                        )}

                        {/* Drawn in the reduced view and in no other. With the embedded HTML view chosen the markup
                            is already on the screen under this line, so a control offering to open it would open a
                            second copy of what is being read — which is why it goes rather than being disabled. */}
                        {embeddedHtml ? null : (
                            <ShowFullHtml
                                onShow={() => {
                                    onShowFullHtml(storedEmailId, message.headers.subject);
                                }}
                            />
                        )}

                        <ReceivedAt at={message.headers.receivedAt} />
                    </div>

                    {/* The gestures a selection ends on rather than a document-wide subscription: a selection made
                        with the pointer settles on the release and one made with the keyboard on the key coming back
                        up, and both of them are events this region already receives. */}
                    {/* The ceiling this pane reads a message's words under, which binds the content alone: the head
                        above it, the verdict about who sent it, the files it carries, and the actions have no measure
                        to keep and take the pane's own width. It is ranged left, so a window wider than the ceiling
                        leaves its margin on the empty side of the pane rather than pushing the words away from the
                        list they were opened from. A conversation answers the same question differently, which is why
                        the measure is stated by each surface rather than inside the message. */}
                    <div ref={body} onKeyUp={capture} onMouseUp={capture} className="max-w-reading">
                        {/* The body being drawn is what opening this message means, so it is what marks it read. The
                            description above already says which account and folder the message is counted in, which is
                            what a folder's unread count is corrected by. */}
                        <Message
                            body={bodyRead}
                            storedEmailId={storedEmailId}
                            onBodyDrawn={() => {
                                markRead({
                                    storedEmailId: message.storedEmailId,
                                    account: message.account,
                                    folder: message.folder,
                                    unread: message.unread,
                                });
                            }}
                        />
                    </div>

                    {message.attachments.length === 0 ? null : (
                        <Attachments
                            session={session}
                            storedEmailId={storedEmailId}
                            attachments={message.attachments}
                        />
                    )}
                </div>

                {message.carried === null ? null : <Carried carried={message.carried} />}
            </div>
        </article>
    );
}

// What a message carries besides its files, where any of it is true. Each of the three is a fact about the message
// rather than about a part, which is why they are said here and not on a row: a signature and an unopened `winmail.dat`
// are not files a reader can open, and drawing them as ones would offer a download that answers with nothing.
function Carried({ carried }: { readonly carried: MailCarried }) {
    const { locale, translate } = useLocalization();

    const notes: readonly MessageKey[] = [
        ...(carried.encrypted ? (['carried.encrypted'] as const) : []),
        ...(carried.unverifiedSignature ? (['carried.unverifiedSignature'] as const) : []),
        ...(carried.unexpandedTnefPart ? (['carried.unexpandedTnefPart'] as const) : []),
    ];

    if (notes.length === 0 && carried.attachmentCount === 0) {
        return null;
    }

    return (
        <aside className="flex flex-col gap-1 text-sm text-muted">
            {carried.attachmentCount === 0 ? null : (
                <p>{translate('carried.total', { size: sizeOf(carried.totalSizeOctets, locale) })}</p>
            )}

            {notes.map((note) => (
                <p key={note}>{translate(note)}</p>
            ))}
        </aside>
    );
}
