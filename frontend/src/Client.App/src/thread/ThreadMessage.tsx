// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientSession, MailFathomTransport, MailThreadMessage } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { MessageMarkers } from '../controls/MessageMarkers';
import { Organisation } from '../controls/Organisation';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SenderAvatar } from '../controls/SenderAvatar';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { Message } from '../messageBody/Message';
import { useMessageBody } from '../messageBody/useMessageBody';
import { drawnUnread, useReadMarking } from '../readMarking/useReadMarking';
import type { ArrivalMark } from './threadOpening';

// One message of a conversation, as a head and what it says. It is its own component for the reason a list row is: it
// is what carries the read and the way out to the message on its own.
//
// It is drawn in the two states the design project draws a message of a conversation in. Open, it is flat on the
// column: the head, the words, and the way to the message on its own. Collapsed, it is the head alone on a bordered
// card, which is what every earlier message is until somebody presses it — so showing the history of a conversation of
// thirty messages puts thirty heads on the screen and reads no body, and nothing already drawn moves while a body
// arrives under it. The head is the control that opens and closes the message, in both states, which is what the
// design draws it as.
//
// It is a region of its own so that arriving at a conversation can place the reader on the message they came for, for
// the reason `readingPane/ReadingPane.tsx` names its opened message: focus has to land on something a screen reader
// announces by more than its tag.
//
// What it is bounded to is the conversation's measure rather than the pane's, and it is centred in the pane: a message
// drawn the whole width of a wide window is a line nobody reads comfortably, and a column of messages has nothing
// beside it to range against. The measure binds the whole message — its head as much as its words — which is why it
// sits here rather than inside the body.

// What a mark says in the head of the message it is on. Two sentences rather than one worded for both, because the two
// are not the same claim: one says a person opened this message, the other that the client brought them to it.
const arrivalLabels: Readonly<Record<ArrivalMark, MessageKey>> = {
    list: 'thread.openedFromList',
    result: 'thread.landedFromResult',
};

// What a mark draws around the message: the accent rule down its edge with everything it says indented past it, and —
// for a landing, which announces itself rather than recording something — the accent tint behind it until it settles.
// A message carrying neither takes no rule and no fill, which is what the design project draws an open message as.
const arrivalStyles: Readonly<Record<ArrivalMark, string>> = {
    list: 'border-s-3 border-s-accent ps-2.75',
    result: 'border-s-3 border-s-accent bg-accent-soft ps-2.75',
};

export function ThreadMessage({
    session,
    transport,
    message,
    mark,
    collapsed,
    onToggle,
    onOpenOnItsOwn,
    onRegion,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly message: MailThreadMessage;

    /** What marks this message out as the one the conversation arrived at, or `null` where nothing does. */
    readonly mark: ArrivalMark | null;

    /** Whether the head stands alone, with the words behind a press on it. */
    readonly collapsed: boolean;

    /** What the head does when pressed: opens a collapsed message, and collapses an open one. */
    readonly onToggle: () => void;

    readonly onOpenOnItsOwn: () => void;
    readonly onRegion: (element: HTMLElement | null) => void;
}) {
    const { translate } = useLocalization();
    const marking = useReadMarking();
    const email = message.email;
    const sender = email.senderDisplayName ?? email.senderAddress ?? translate('list.senderUnknown');

    // A collapsed message wants no read, which is what keeps a conversation of thirty heads to one body: the head is
    // drawn from what the conversation already answered with, and pressing it is what asks for the words.
    const body = useMessageBody(session, transport, email.id, !collapsed);

    // What the deployment last reported, less what this client has marked read since — the same reading the list's own
    // row draws from, because the two are the same message in two places and a reader who opened it here would
    // otherwise find it still unread there.
    const unread = drawnUnread(marking, email.id, email.unread);

    return (
        <li>
            <article
                ref={onRegion}
                tabIndex={-1}
                aria-label={translate('thread.messageBy', { sender })}
                className={`mx-auto flex w-full max-w-conversation flex-col gap-2.75 transition ${
                    collapsed ? 'rounded-xl border border-line bg-sunken px-3 py-2' : ''
                } ${mark === null ? '' : arrivalStyles[mark]}`}
            >
                <button
                    type="button"
                    aria-expanded={!collapsed}
                    className="flex w-full items-center gap-2.75 rounded-md text-start transition hover:bg-hover"
                    onClick={onToggle}
                >
                    <SenderAvatar displayName={email.senderDisplayName} address={email.senderAddress} place="card" />

                    {unread ? <span className="sr-only">{translate('list.unread')}</span> : null}

                    {/* Who wrote is what a conversation is scanned by, so it keeps its width and everything beside it
                        is what gives way. */}
                    <span className={`shrink-0 text-md font-semibold ${unread ? 'text-text' : 'text-text-soft'}`}>
                        {sender}
                    </span>

                    {/* What the mark says, so that the message the conversation arrived at is named rather than only
                        tinted: a rule down an edge is invisible to somebody who is being read to, and the accent is
                        invisible to somebody who cannot tell it from the text beside it. */}
                    {mark === null ? null : (
                        <span className="flex shrink-0 items-center gap-1 rounded-sm bg-accent-soft px-1.75 py-0.5 text-2xs tracking-wide whitespace-nowrap text-accent-deep">
                            <Icon name="arrow_right" className="size-3" />
                            {translate(arrivalLabels[mark])}
                        </span>
                    )}

                    <Organisation address={email.senderAddress} />

                    <MessageMarkers email={email} />

                    <ReceivedAt at={email.receivedAt} />
                </button>

                {collapsed ? null : (
                    <>
                        <p className="text-sm text-muted">
                            {translate('thread.storedIn', { account: email.account, folder: email.folder })}
                        </p>

                        {/* Every body the conversation drew is marked read, which is one rule rather than two: the
                            reading pane and a message here put the same words in front of the same person, and what
                            marks a message read is that its body was drawn wherever it was drawn. Nothing the screen
                            decides for itself draws more than the latest message — the rest are collapsed until
                            pressed, which is a gesture the reader makes knowing whose message it opens. */}
                        <Message
                            body={body}
                            storedEmailId={email.id}
                            quotedHistoryOnRequest
                            onBodyDrawn={() => {
                                marking.markRead({
                                    storedEmailId: email.id,
                                    account: email.account,
                                    folder: email.folder,
                                    unread: email.unread,
                                });
                            }}
                        />

                        <div>
                            <SecondaryButton label={translate('thread.openOnItsOwn')} onActivate={onOpenOnItsOwn} />
                        </div>
                    </>
                )}
            </article>
        </li>
    );
}
