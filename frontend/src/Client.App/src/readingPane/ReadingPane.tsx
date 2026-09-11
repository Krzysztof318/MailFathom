// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    readMailMessage,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailMessage,
    type SignalledFlags,
} from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useSignalledChanges } from '../signals/signalledChanges';
import { useOpenAttachment } from '../workspace/openAttachment';
import { useWorkspace } from '../workspace/useWorkspace';
import { MessageWaiting } from '../messageBody/Message';
import { useMessageBody } from '../messageBody/useMessageBody';
import { BackToList } from '../mailSpace/BackToList';
import { NothingOpen } from '../mailSpace/NothingOpen';
import { useTwoPanes } from '../shell/useWideWorkspace';
import { MessageHeaders } from './MessageHeaders';
import { OpenedMessage } from './OpenedMessage';

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
    missing: 'failure.missing',
};

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

/**
 * The answer with the flags the deployment stated applied to it, or the answer unchanged where it is about another
 * message or is not a message at all.
 *
 * A flag the statement is silent about is one the deployment did not observe rather than one it cleared, so the message
 * keeps what it was drawn with — which is what makes applying the same statement twice the same screen.
 */
function flagsApplied(answered: Answered | null, storedEmailId: string, flags: SignalledFlags): Answered | null {
    if (answered?.result.outcome !== 'read' || answered.result.value.storedEmailId !== storedEmailId) {
        return answered;
    }

    const message = answered.result.value;

    return {
        ...answered,
        result: {
            ...answered.result,
            value: {
                ...message,
                unread: flags.isSeen === null ? message.unread : !flags.isSeen,
                flagged: flags.isFlagged ?? message.flagged,
            },
        },
    };
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
    const { translate } = useLocalization();
    const twoPanes = useTwoPanes();
    const { workspace, revise } = useWorkspace();
    const openAttachment = useOpenAttachment();
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

            // A quiet read the deployment did not answer leaves the message it was reading again where it stands: what a
            // reader is part-way through is still the truest thing anybody has, and the next signal or refresh asks
            // again. `missing` is not that — it is the deployment saying the message is gone — and is let go of below.
            setAnswer((current) =>
                read.quietly &&
                answered.outcome === 'failed' &&
                answered.failure.reason !== 'missing' &&
                current?.result.outcome === 'read' &&
                current.read.storedEmailId === read.storedEmailId
                    ? current
                    : { read, result: answered },
            );

            // A message the deployment no longer holds is let go of rather than drawn as a failure to press through.
            // What is open outlives the message across a reload, so somebody returning to a client whose message has
            // since been deleted or moved lands on the empty state they would have had if nothing had been open — and
            // somebody whose open message is deleted elsewhere while they read it lands there too, which is the same
            // fact arriving a moment later.
            //
            // It is only ever `missing` that does this, and that is the whole reason the reason exists: every other
            // failure may answer differently on the next attempt, and closing what a reader had open because their
            // deployment blinked would lose their place for a fault that is about to pass. The read that answered is
            // the message that is open — a message changed under this component ends this attempt above rather than
            // reaching here — so nothing has to be compared against what is selected now.
            if (answered.outcome === 'failed' && answered.failure.reason === 'missing') {
                revise({ selection: null });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, read, online, revise]);

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
    // not this message's business: the list it names re-reads its own rows. A refresh is every message's business, and
    // reads this one again the same way.
    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (
                    signal.kind === 'refresh' ||
                    (signal.kind === 'mail.changed' && signal.emails.includes(storedEmailId))
                ) {
                    setRead((current) => ({ storedEmailId, attempt: current.attempt + 1, quietly: true }));
                }

                // A flag is applied to what is already drawn rather than read again: the two values it moves are the
                // two this pane draws, so a star or a read mark from anywhere lands here without a round trip. A
                // statement that arrives while a read is in flight is overwritten by that read's answer, which is the
                // newer of the two readings and the one this pane asked for.
                if (signal.kind === 'mail.flags.changed') {
                    const flags = signal.flags.find((stated) => stated.email === storedEmailId);

                    if (flags !== undefined) {
                        setAnswer((current) => flagsApplied(current, storedEmailId, flags));
                    }
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

                {/* Said out of sight rather than not said: the shapes below are what a reader looking at the pane
                    sees, and this is the same statement for somebody who is not. */}
                <p className="sr-only" role="status">
                    {translate('message.reading')}
                </p>

                <MessageWaiting oneColumn={!twoPanes} />
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

                {/* Reading again is the way out of exactly one of the five failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other four repeat identically on a second attempt. */}
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
            <MessageHeaders headers={message.headers} message={message} />

            <div className="flex flex-col gap-3 px-5.5 py-4.5">
                <OpenedMessage
                    session={session}
                    message={message}
                    body={bodyRead}
                    onShowFullHtml={() => {
                        onShowFullHtml(storedEmailId, message.headers.subject);
                    }}
                />
            </div>
        </article>
    );
}
