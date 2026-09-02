// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
    readMailThread,
    type ClientFailure,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
    type MailThreadPage,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { OpenConversation } from '../workspace/openConversation';
import { useWorkspace } from '../workspace/useWorkspace';
import { holdsMessage, messagesOf, openedBy } from './threadOpening';
import { ThreadMessage } from './ThreadMessage';

// A conversation, which is the unit people actually think in and the one mail screen no folder is the scope of: the
// question is in the inbox, the answer is in the sent folder, and the service reads across both. What this screen owns
// is the presentation, and the presentation is the whole difficulty — a long conversation drawn naively is the same
// paragraph eight times.
//
// Three things answer that. Each message is a line until somebody opens it, and opening it is what mounts its body, so
// a conversation of thirty messages costs one read rather than thirty-one. What each message added is what the line
// shows, trimmed of the history it quoted by the deployment rather than here. And the history that is quoted inside an
// opened message is folded away behind a disclosure, because the message it quotes is a row of its own a few lines up.
//
// It stands in front of the message it was opened from rather than replacing it: the workspace still holds that
// message, so closing the conversation returns to it and the place it returns to is still there.
//
// Nothing here windows the rows, and that is a decision rather than an omission. The service assembles at most five
// hundred messages of a conversation, a page holds a hundred of them, and every page past the first is one the reader
// asked for — so the document holds a screenful of one-line rows plus whatever they opened, rather than a mailbox. The
// arithmetic the message list windows with does not transfer either: it is one row height, and an opened message is as
// tall as what it says. A conversation that ever renders slowly is the argument for reopening this.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

export function Thread({
    session,
    transport,
    conversation,
    online,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly conversation: OpenConversation;
    readonly online: boolean;
}) {
    const { locale, translate } = useLocalization();
    const { revise } = useWorkspace();

    const [pages, setPages] = useState<readonly MailThreadPage[]>([]);
    const [failure, setFailure] = useState<ClientFailure | null>(null);
    const [asked, setAsked] = useState(false);
    const [connected, setConnected] = useState(online);

    // What is open, once the conversation has decided it. `null` until then, which is what tells the decision apart
    // from a reader having closed everything — and after that it is theirs rather than this screen's.
    const [expanded, setExpanded] = useState<readonly string[] | null>(null);

    const summaries = useRef(new Map<string, HTMLElement>());

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
    const latest = pages.at(-1) ?? null;

    // A conversation opened at a message is read forward until that message is in hand, because the route pages from
    // the beginning and the surrounding history is what somebody arriving from a search result came for. The count the
    // answer states is what bounds that: a deployment answering with cursors and no progress stops the search rather
    // than driving it forever.
    const arrived = conversation.openAt === null || holdsMessage(held, conversation.openAt);
    const searching = !arrived && latest !== null && held.length < latest.messageCount;

    const wanted = pageWanted(latest, online && failure === null, asked || searching);
    const wantedCursor = wanted?.cursor ?? null;
    const reading = wanted !== null;

    // What the conversation opens with is decided the moment it has stopped reading, from what is held then, and never
    // again: a page arriving later would otherwise close the message somebody is reading and open a newer one under
    // their cursor. React's answer to a value that becomes decidable is to adjust state during the render it became
    // decidable in rather than in an effect that would draw the undecided screen once first.
    //
    // A search still in progress is not a conversation that has stopped reading, which is why it is asked about
    // separately: a failed page or a network gap stops the reading without ending the search, and deciding there would
    // settle the question from half a conversation and never reopen it — so a reader who came for a message and pressed
    // *Read again* would arrive at whatever the half held instead. The count the answer states is what ends the search
    // where the message is genuinely not there, so this waits for an answer rather than for the message.
    if (expanded === null && latest !== null && !reading && !searching) {
        setExpanded(openedBy(held, conversation.openAt));
    }

    // The one effect that puts a request on the wire, which is what an effect is for. An answer to a read this screen
    // has moved on from is discarded rather than cancelled, for the reason the reading pane gives.
    useEffect(() => {
        if (!reading) {
            return;
        }

        let listening = true;

        void readMailThread(session, transport, conversation.threadId, wantedCursor).then((result) => {
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

    // Arriving in a conversation is a view change, so focus goes to the message it opened at rather than staying on
    // whatever opened the conversation. Focus rather than a scroll of our own: placing it is the obligation, a browser
    // scrolls what it focuses into view, and one call cannot leave the two disagreeing about where the reader is.
    //
    // It is placed once, on arriving, and never again. `expanded` is what a reader changes for the rest of the visit —
    // every message they open or collapse is a new value of it — and re-placing focus on those would take it off the
    // disclosure they just operated and put it somewhere they did not ask to be. Collapsing an open message is not a
    // view change. Arriving somewhere else is, and that is a conversation of its own: `App.tsx` keys this component by
    // the conversation together with the message it was opened at, so a different arrival is a different mount.
    useEffect(() => {
        if (expanded === null || arrivedAt.current !== null) {
            return;
        }

        // Where the reader is arriving was decided when the conversation decided what to open: the message somebody
        // named, where the conversation holds it, and otherwise the first of what it opened for itself — which is what
        // a message named but never found leaves, and what an arrival with nobody named leaves too. Reading it back
        // from what is open rather than from what was asked for is what keeps focus and the screen from disagreeing. A
        // conversation holding no message at all has nothing to arrive at, and the empty state says so where focus
        // already is.
        const arriveAt = expanded.at(0) ?? null;

        if (arriveAt === null) {
            return;
        }

        const summary = summaries.current.get(arriveAt);

        if (summary !== undefined) {
            arrivedAt.current = arriveAt;
            summary.focus();
        }
    }, [expanded]);

    function close(): void {
        revise({ conversation: null });
    }

    function openOnItsOwn(storedEmailId: string): void {
        revise({ conversation: null, selection: storedEmailId });
    }

    function show(storedEmailId: string, open: boolean): void {
        setExpanded((current) => {
            const shown = current ?? [];

            if (open) {
                return shown.includes(storedEmailId) ? shown : [...shown, storedEmailId];
            }

            return shown.filter((one) => one !== storedEmailId);
        });
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

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
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

    return (
        <Conversation
            onClose={close}
            header={
                <header className="flex flex-col gap-1.5 border-b border-line px-5.5 py-4">
                    <h2 className="text-3xl font-semibold text-balance">
                        {held[0]?.email.subject ?? translate('message.noSubject')}
                    </h2>

                    {/* Everybody who wrote, from the answer rather than walked out of the messages in hand: they are
                        the conversation's authors, so a screen deriving them would be paging a conversation to draw
                        its header. The list is worded by `Intl` under the active locale rather than joined here. */}
                    {latest.participants.length === 0 ? null : (
                        <p className="text-base text-text-soft">
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
                <ol className="flex flex-col gap-3">
                    {held.map((message) => (
                        <ThreadMessage
                            key={message.email.id}
                            session={session}
                            transport={transport}
                            message={message}
                            expanded={expanded?.includes(message.email.id) ?? false}
                            onExpanded={(open) => {
                                show(message.email.id, open);
                            }}
                            onOpenOnItsOwn={() => {
                                openOnItsOwn(message.email.id);
                            }}
                            onSummary={(element) => {
                                if (element === null) {
                                    summaries.current.delete(message.email.id);
                                } else {
                                    summaries.current.set(message.email.id, element);
                                }
                            }}
                        />
                    ))}
                </ol>
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
                    <SecondaryButton
                        label={translate('thread.readMore')}
                        onActivate={() => {
                            setAsked(true);
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
    children,
}: {
    readonly onClose: () => void;
    readonly header?: ReactNode;
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

            <div className="flex flex-col gap-3 px-5.5 py-4.5">{children}</div>
        </section>
    );
}
