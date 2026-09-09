// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
    readMailThread,
    readMailThreadState,
    type ClientFailure,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailThreadPage,
    type MailThreadState,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { HeadActs } from '../mailSpace/HeadActs';
import { useTwoPanes } from '../shell/useWideWorkspace';
import type { OpenConversation } from '../workspace/openConversation';
import { useWorkspace } from '../workspace/useWorkspace';
import { arrivalMark, arrivesAt, holdsMessage, messagesOf, type Arrival } from './threadOpening';
import { ThreadMessage } from './ThreadMessage';
import { ThreadState } from './ThreadState';

// A conversation, which is the unit people actually think in and the one mail screen no folder is the scope of: the
// question is in the inbox, the answer is in the sent folder, and the service reads across both. What this screen owns
// is the presentation, and the presentation is the whole difficulty — a long conversation drawn naively is the same
// paragraph eight times.
//
// Three things answer that. The conversation shows the message it was opened at and hides everything else behind one
// control naming how many there are, so opening a long conversation is reading the message somebody came for rather
// than first scrolling past eight of them — and pressing that control draws every one of them out in full, because a
// conversation is a document rather than a list of things to open one at a time. What each message says is trimmed of
// the history it quoted by the deployment rather than here. And the history that is quoted inside a message is folded
// away behind a disclosure, because the message it quotes is a message of its own a few lines up.
//
// **The whole correspondence arrives in one request**, messages and bodies together, so revealing the history costs
// nothing on the wire and the setting that opens a conversation expanded costs nothing either. That is what bounds the
// page: a read carrying the messages themselves is held to the deployment's own content-read ceiling, and a
// correspondence longer than one page is read on with the control below it.
//
// It stands in front of the message it was opened from rather than replacing it: the workspace still holds that
// message, so closing the conversation returns to it and the place it returns to is still there.
//
// Nothing here windows the rows, and that is a decision rather than an omission. The service assembles at most five
// hundred messages of a conversation, a page holds a hundred of them, and every page past the first is one the reader
// asked for — so the document holds a screenful of one-line rows plus whatever they opened, rather than a mailbox. The
// arithmetic the message list windows with does not transfer either: it is one row height, and an opened message is as
// tall as what it says. A conversation that ever renders slowly is the argument for reopening this.

// How long a message landed on from a search result stays marked before it settles into an ordinary open message. The
// design project's own dwell rather than a transition duration, which is why it is a number here and not a token: it is
// how long something is said for, and the theme decides how movement happens rather than how long a screen speaks.
const landingHeldFor = 2_200;

// What the one control over the correspondence says, which is a question about two things rather than one: whether the
// rest of it is standing, and whether the message it was opened at is the newest. A conversation opened in the middle
// of its history hides messages on both sides of that one, so what it offers is the whole correspondence rather than
// the earlier part of it — the design project words all four, and each is a different sentence rather than a wording of
// one.
//
// Three of the four are one sentence each. The fourth counts, so it is a form per plural category rather than a number
// appended to a sentence: Polish words one earlier message, two, and five differently, and the design project writes
// all three out.
const earlierHidden: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'thread.showEarlier.other',
    one: 'thread.showEarlier.one',
    two: 'thread.showEarlier.other',
    few: 'thread.showEarlier.few',
    many: 'thread.showEarlier.many',
    other: 'thread.showEarlier.other',
};

function revealLabel(historyShown: boolean, openedIsLatest: boolean, hidden: number, locale: string): MessageKey {
    if (historyShown) {
        return openedIsLatest ? 'thread.hideEarlier' : 'thread.hideOthers';
    }

    return openedIsLatest ? earlierHidden[new Intl.PluralRules(locale).select(hidden)] : 'thread.showAll';
}

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

export function Thread({
    session,
    transport,
    conversation,
    online,
    expandWholeThread,
    onShowFullHtml,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly conversation: OpenConversation;
    readonly online: boolean;

    /** Whether the reader asked for conversations to open with every message drawn rather than at the one they came for. */
    readonly expandWholeThread: boolean;

    /** Opens the surface drawing one message's own markup, which each message of the conversation offers. */
    readonly onShowFullHtml: (storedEmailId: string, subject: string | null) => void;
}) {
    const { locale, translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const twoPanes = useTwoPanes();
    const panelsHidden = workspace.panelsHidden;

    const [pages, setPages] = useState<readonly MailThreadPage[]>([]);

    // Where the conversation stands, as the deployment derived it, and `null` while the block is still being read. It
    // is a read of its own rather than part of the conversation's pages: the deployment wrote it behind an account run
    // and serves it from a route of its own, so a conversation of eight pages still costs one read of it.
    const [derivation, setDerivation] = useState<ClientResult<MailThreadState | null> | null>(null);

    // The message a statement's source was followed to, and which press followed it. The press is part of the value so
    // that following the same source twice is two view changes rather than one: focus is placed on each of them.
    const [followed, setFollowed] = useState<{ readonly storedEmailId: string; readonly press: number } | null>(null);
    const [failure, setFailure] = useState<ClientFailure | null>(null);
    const [asked, setAsked] = useState(false);
    const [connected, setConnected] = useState(online);

    // Where the reader arrived, once the conversation has decided it, and `null` until then. It is what focus is
    // placed on and what the mark is decided from, and it never moves afterwards.
    const [arrival, setArrival] = useState<Arrival | null>(null);

    // Whether a landing has had its time. It starts unsettled and is never unset, because settling is what a landing
    // does: nothing in a conversation brings one back, and a conversation reached a second time is a second mount.
    const [settled, setSettled] = useState(false);

    // Whether the rest of the correspondence is drawn beside the message it was opened at. It opens on what the reader
    // asked conversations to open on and is theirs from then on. The preference is read once, on mounting, because it
    // says how a conversation *opens*: a switch moved while one is on the screen changes the next conversation rather
    // than this one, which is also why the control below still hides a correspondence the preference showed.
    const [historyShown, setHistoryShown] = useState(expandWholeThread);

    const regions = useRef(new Map<string, HTMLElement>());

    // Whether arriving in this conversation has already put the reader somewhere. A ref rather than a flag in state
    // because it survives StrictMode's second invocation of the effect below, for the reason the reading pane's own
    // focus guard is one.
    const arrivedAt = useRef<string | null>(null);

    // A failure the network gap itself caused goes with the gap, so what stands while there is no network is the
    // sentence below rather than a refusal to try again that a reader would have to press through — and coming back
    // reads again on its own, which is what makes that sentence's promise a true one. Adjusted during render, which is
    // where React answers a changed prop, for the reason `readingPane/ReadingPane.tsx` gives.
    if (connected !== online) {
        setConnected(online);

        if (!online) {
            setFailure(null);
        }
    }

    const held = messagesOf(pages);
    const mark = arrivalMark(conversation, arrival, settled);
    const latest = pages.at(-1) ?? null;

    // What stands on the screen: the whole correspondence, or the one message it was opened at. The design draws the
    // second as the message somebody came for and nothing else — which is not the same as the latest message, because a
    // conversation opened at a message in the middle of its history was opened at that one.
    const opened = held.find((message) => message.email.id === arrival?.storedEmailId) ?? held.at(-1) ?? null;
    const drawn = historyShown || opened === null ? held : [opened];
    const openedIsLatest = opened !== null && opened.email.id === held.at(-1)?.email.id;

    // A conversation opened at a message is read forward until that message is in hand, because the route pages from
    // the beginning and the surrounding history is what somebody arriving from a search result came for. The count the
    // answer states is what bounds that: a deployment answering with cursors and no progress stops the search rather
    // than driving it forever.
    const arrived = conversation.openAt === null || holdsMessage(held, conversation.openAt);
    const searching = !arrived && latest !== null && held.length < latest.messageCount;

    const wanted = pageWanted(latest, online && failure === null, asked || searching);
    const wantedCursor = wanted?.cursor ?? null;
    const reading = wanted !== null;

    // Where the conversation puts the reader is decided the moment it has stopped reading, from what is held then, and
    // never again: a page arriving later would otherwise move them off the message they came for. React's answer to a
    // value that becomes decidable is to adjust state during the render it became decidable in rather than in an
    // effect that would draw the undecided screen once first — which here would be the history drawn hidden and then
    // shown under a reader who arrived inside it.
    //
    // A search still in progress is not a conversation that has stopped reading, which is why it is asked about
    // separately: a failed page or a network gap stops the reading without ending the search, and deciding there would
    // settle the question from half a conversation and never reopen it — so a reader who came for a message and pressed
    // *Read again* would arrive at whatever the half held instead. The count the answer states is what ends the search
    // where the message is genuinely not there, so this waits for an answer rather than for the message.
    if (arrival === null && !reading && !searching) {
        const arriveAt = arrivesAt(held, conversation.openAt);

        if (arriveAt !== null) {
            setArrival(arriveAt);
        }
    }

    // The one effect that puts a request on the wire, which is what an effect is for. An answer to a read this screen
    // has moved on from is discarded rather than cancelled, for the reason the reading pane gives.
    useEffect(() => {
        if (!reading) {
            return;
        }

        let listening = true;

        void readMailThread(session, transport, conversation.threadId, wantedCursor, true).then((result) => {
            if (!listening) {
                return;
            }

            if (result.outcome === 'failed') {
                setFailure(result.failure);
            } else {
                setPages((current) => [...current, result.value]);
                setAsked(false);
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, conversation.threadId, reading, wantedCursor]);

    // Where the conversation stands is read once per conversation, because that is what it is: a record the deployment
    // wrote behind an account run rather than something composed while this screen waits. A machine with no network
    // reads nothing and the block says it is still reading, which is what the frame above already explains.
    useEffect(() => {
        if (!online) {
            return;
        }

        let listening = true;

        void readMailThreadState(session, transport, conversation.threadId).then((result) => {
            if (listening) {
                setDerivation(result);
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, conversation.threadId, online]);

    // Following a statement's source is a view change, so focus goes to the message it named. It is placed here rather
    // than in the press because the message may be one the history was hiding, and the element does not exist until
    // the render that showed it. The sheet a phone follows a source from closes in an effect of its own, and a child's
    // effects run before its parent's, so the focus this places is the one that stands.
    useEffect(() => {
        if (followed === null) {
            return;
        }

        regions.current.get(followed.storedEmailId)?.focus();
    }, [followed]);

    // Arriving in a conversation is a view change, so focus goes to the message it opened at rather than staying on
    // whatever opened the conversation. Focus rather than a scroll of our own: placing it is the obligation, a browser
    // scrolls what it focuses into view, and one call cannot leave the two disagreeing about where the reader is.
    //
    // It is placed once, on arriving, and never again. Showing and hiding the history is what a reader does for the
    // rest of the visit, and re-placing focus on that would take it off the control they just operated and put it
    // somewhere they did not ask to be. Arriving somewhere else is a view change, and that is a conversation of its
    // own: `App.tsx` keys this component by the conversation together with the message it was opened at, so a
    // different arrival is a different mount.
    //
    // A conversation holding no message has nothing to arrive at, and the empty state says so where focus already is.
    useEffect(() => {
        if (arrival === null || arrivedAt.current !== null) {
            return;
        }

        const region = regions.current.get(arrival.storedEmailId);

        if (region !== undefined) {
            arrivedAt.current = arrival.storedEmailId;
            region.focus();
        }
    }, [arrival]);

    // A landing says the client took somebody where they asked to go, so it is timed from the message being on the
    // screen rather than from the conversation being opened: a mark that ran out while the conversation was still
    // being read would have marked nothing anybody saw. A timer is something outside React, which is what an effect is
    // for, and it goes with the screen it was started for.
    useEffect(() => {
        if (arrival === null || conversation.fromResult !== true) {
            return;
        }

        const settling = setTimeout(() => {
            setSettled(true);
        }, landingHeldFor);

        return () => {
            clearTimeout(settling);
        };
    }, [arrival, conversation.fromResult]);

    function close(): void {
        revise({ conversation: null });
    }

    function openOnItsOwn(storedEmailId: string): void {
        revise({ conversation: null, selection: storedEmailId });
    }

    function followSource(storedEmailId: string): void {
        setHistoryShown(true);
        setFollowed((last) => ({ storedEmailId, press: (last?.press ?? 0) + 1 }));
    }

    // Offline is its own sentence rather than a failure worded politely, and it is said only where there is nothing to
    // draw instead: a conversation already on the screen is the truest thing anybody has, and the frame above already
    // says the machine has no network.
    if (!online && latest === null) {
        return (
            <Conversation onClose={close}>
                <p className="text-sm text-muted" role="status">
                    {translate('thread.offline')}
                </p>
            </Conversation>
        );
    }

    if (latest === null && failure !== null) {
        return (
            <Conversation onClose={close}>
                <p className="text-sm text-warning" role="alert">
                    {translate('thread.failed', { reason: translate(failureLabels[failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the five failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other four repeat identically on a second attempt. */}
                {failure.reason === 'unavailable' ? (
                    <SecondaryButton
                        label={translate('connection.retry')}
                        onActivate={() => {
                            setFailure(null);
                        }}
                    />
                ) : null}
            </Conversation>
        );
    }

    if (latest === null) {
        return (
            <Conversation onClose={close}>
                <p className="text-sm text-muted" role="status">
                    {translate('thread.reading')}
                </p>
            </Conversation>
        );
    }

    // A state this deployment could not answer for is drawn as nothing at all rather than as a second failure line: the
    // block stands beside the conversation rather than inside it, and a reader who came to read the mail can act on
    // neither the absence nor the reason. What a read that answered with nothing draws is the absence itself, which is
    // a state the block says in a sentence.
    const derived = derivation?.outcome === 'read' ? derivation.value : null;
    const stateBlock =
        derivation?.outcome === 'failed' ? undefined : (
            <ThreadState state={derived} reading={derivation === null} messages={held} onFollowSource={followSource} />
        );

    return (
        <Conversation
            onClose={close}
            state={stateBlock}
            header={
                // The head goes with the panels where the composition has two of them, which is the design's own
                // arithmetic: `showThreadHead` is off under the *fullscreen* control except in a single pane, where
                // the head is also what carries the way back to the list.
                panelsHidden && twoPanes ? undefined : (
                    <header className="flex flex-col gap-1.75 border-b border-line px-5.5 py-4">
                        {/* The acts the design draws beside a conversation's subject, the same four the head of a
                            message carries: a conversation is what they are about in the design, whichever message
                            of it is on the screen — which is why none of them is handed a message here, and why each
                            stands as what it is. `mailSpace/HeadActs.tsx` holds what a conversation would need first. */}
                        <div className="flex min-w-0 items-center gap-2.25">
                            <h2 className="min-w-0 flex-1 text-3xl font-semibold text-balance">
                                {held[0]?.email.subject ?? translate('message.noSubject')}
                            </h2>

                            <HeadActs compact={!twoPanes} message={null} />
                        </div>

                        {/* Everybody who wrote, from the answer rather than walked out of the messages in hand: they are
                            the conversation's authors, so a screen deriving them would be paging a conversation to draw
                            its header. The list is worded by `Intl` under the active locale rather than joined here. */}
                        {latest.participants.length === 0 ? null : (
                            <p className="text-base text-muted">
                                {translate('thread.wroteHere', {
                                    names: new Intl.ListFormat(locale, { type: 'conjunction' }).format(
                                        latest.participants.map((one) => one.displayName ?? one.address),
                                    ),
                                })}
                            </p>
                        )}

                        {latest.moreParticipantsNotNamed ? (
                            <p className="text-base text-muted">{translate('thread.moreParticipants')}</p>
                        ) : null}

                        <p className="text-base text-muted">
                            {translate('thread.messages', {
                                count: new Intl.NumberFormat(locale).format(latest.messageCount),
                            })}
                        </p>

                        {latest.moreMessagesNotAssembled ? (
                            <p className="text-base text-warning">{translate('thread.moreNotAssembled')}</p>
                        ) : null}
                    </header>
                )
            }
        >
            {/* A read that failed with messages already drawn is the partial state: what is on the screen stays, and
                what is missing is said above it rather than replacing it. */}
            {failure === null ? null : (
                <div className="flex flex-col items-start gap-2">
                    <p className="text-sm text-warning" role="alert">
                        {translate('thread.partiallyFailed', { reason: translate(failureLabels[failure.reason]) })}
                    </p>

                    {failure.reason === 'unavailable' ? (
                        <SecondaryButton
                            label={translate('connection.retry')}
                            onActivate={() => {
                                setFailure(null);
                            }}
                        />
                    ) : null}
                </div>
            )}

            {/* Nothing in hand is two different things, and saying the wrong one is worse than saying nothing: a page
                that held no message a reader may see while the next one is already on the wire is a conversation still
                being read, and calling that empty is a screen that looks finished mid-read. The sentence below the list
                says the same thing for a conversation that has something to show, which is why it does not say it
                here. */}
            {held.length === 0 ? (
                <p className="text-sm text-muted" role="status">
                    {translate(reading ? 'thread.reading' : 'thread.empty')}
                </p>
            ) : (
                <>
                    {/* The whole history behind one control, which is what a conversation of eight messages is
                        otherwise eight decisions about. It names how many are behind it, so pressing it is a choice
                        rather than a guess, and it stands above them because that is where the design project draws
                        it — between the head of the conversation and the messages themselves. A conversation of one
                        message has no history to offer, and no control.

                        It says four things rather than two, as the design does, because what it hides is *the rest of
                        the correspondence* rather than what came before: a conversation opened at a message in the
                        middle of its history has messages on both sides of it, and a control offering to show the
                        earlier ones there would be offering something other than what it does. */}
                    {held.length < 2 ? null : (
                        <div className="flex items-center gap-2.5">
                            <span className="h-px flex-1 bg-line" />

                            <button
                                type="button"
                                aria-expanded={historyShown}
                                className="rounded-full border border-line bg-sunken px-3 py-1.25 text-sm text-muted transition hover:bg-hover"
                                onClick={() => {
                                    setHistoryShown(!historyShown);
                                }}
                            >
                                {translate(revealLabel(historyShown, openedIsLatest, held.length - 1, locale), {
                                    count: new Intl.NumberFormat(locale).format(held.length - 1),
                                })}
                            </button>

                            <span className="h-px flex-1 bg-line" />
                        </div>
                    )}

                    <ol className="flex flex-col gap-4.5">
                        {drawn.map((message) => (
                            <ThreadMessage
                                key={message.email.id}
                                session={session}
                                transport={transport}
                                message={message}
                                mark={message.email.id === arrival?.storedEmailId ? mark : null}
                                online={online}
                                onOpenOnItsOwn={() => {
                                    openOnItsOwn(message.email.id);
                                }}
                                onShowFullHtml={() => {
                                    onShowFullHtml(message.email.id, message.email.subject);
                                }}
                                onRegion={(element) => {
                                    if (element === null) {
                                        regions.current.delete(message.email.id);
                                    } else {
                                        regions.current.set(message.email.id, element);
                                    }
                                }}
                            />
                        ))}
                    </ol>
                </>
            )}

            {/* A conversation longer than one page says so and is read on, rather than being cut off at the page the
                service serves. Where every page has been read it says that instead, so the end of a conversation and a
                conversation with more to come are two different sentences. */}
            {reading && held.length > 0 ? (
                <p className="text-sm text-muted" role="status">
                    {translate('thread.readingMore')}
                </p>
            ) : null}

            {latest.nextCursor === null ? (
                <p className="text-sm text-faint">{translate('thread.wholeConversationRead')}</p>
            ) : (
                <div>
                    {/* Reading further shows the history with it, and that is a correctness rule rather than a
                        convenience. A page arriving moves the conversation's latest message on, so a history left
                        hidden would take the message the reader is standing on out of what is drawn and unmount the
                        element focus was placed on — and focus is placed once, so nothing would put it back. Asking
                        for more of a conversation is asking to see more of it, so the answer is to keep what is
                        already there rather than to replace it, and the control above still hides it again. This is
                        the only way a page arrives after the reader has arrived: the search that pages forward to a
                        named message ends before the arrival is decided. */}
                    <SecondaryButton
                        label={translate('thread.readMore')}
                        onActivate={() => {
                            setAsked(true);
                            setHistoryShown(true);
                        }}
                    />
                </div>
            )}
        </Conversation>
    );
}

/**
 * The page a conversation still wants, or `null` where it wants none — which is also how a read in flight reads.
 *
 * A network gap and a failure both stop it, which ends the read they interrupted rather than letting it outlive them.
 *
 * @param latest The most recent page held, or `null` where nothing has been read yet.
 * @param reading Whether this screen may read at all.
 * @param continuing Whether it wants the page after the one it holds.
 * @returns Where to read from, or `null`.
 */
function pageWanted(
    latest: MailThreadPage | null,
    reading: boolean,
    continuing: boolean,
): { readonly cursor: string | null } | null {
    if (!reading) {
        return null;
    }

    if (latest === null) {
        return { cursor: null };
    }

    return latest.nextCursor !== null && continuing ? { cursor: latest.nextCursor } : null;
}

// The frame every state of this screen is drawn in, which is what makes the way out of it present in all five: a
// conversation that failed, one with no network, and one still being read each stand under the control that closes it.
// The header stands across the column, as the design project draws a conversation's head, and everything under it
// stands inset from the edges.
function Conversation({
    onClose,
    header,
    state,
    children,
}: {
    readonly onClose: () => void;
    readonly header?: ReactNode;

    /** Where the conversation stands, which stands across the column between its head and its messages. */
    readonly state?: ReactNode;

    readonly children: ReactNode;
}) {
    const { translate } = useLocalization();

    return (
        <section aria-label={translate('thread.label')} className="flex flex-col">
            <div className="px-3.5 pt-3">
                <button
                    type="button"
                    className="flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-base text-text-soft transition hover:bg-hover"
                    onClick={onClose}
                >
                    <Icon name="arrow_back" className="size-5" />
                    {translate('thread.close')}
                </button>
            </div>

            {header}

            {state}

            <div className="flex flex-col gap-3 px-5.5 py-4.5">{children}</div>
        </section>
    );
}
