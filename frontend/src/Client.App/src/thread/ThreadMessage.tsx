// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientSession, MailFathomTransport, MailThreadMessage } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useMessageBody } from '../messageBody/useMessageBody';
import { OpenedMessage } from '../readingPane/OpenedMessage';
import type { ArrivalMark } from './threadOpening';

// One message of a conversation, drawn out in full — which is the whole of what this component decides, because what a
// message looks like is `readingPane/OpenedMessage.tsx`'s and is the same drawing wherever a message is drawn.
//
// **Nothing here collapses.** The design project draws a conversation as one document with every message in it written
// out, and it hands the mail screen `showExpandAll: false` — so there is no per-message control, and what a reader
// decides is how much of the correspondence stands in front of them rather than which message of it is open. The
// conversation's own head is where that decision is made, and this is what it reveals.
//
// The body comes with the conversation rather than from a read of its own: one request carries every message of the
// correspondence, so revealing the history costs nothing on the wire. A message whose local copy the deployment could
// not open arrives without one, and that is the one case this reads a body itself — a gap in the correspondence the
// reader can still be shown rather than a message drawn empty.
//
// It is a region of its own so that arriving at a conversation can place the reader on the message they came for, for
// the reason `readingPane/ReadingPane.tsx` names its opened message: focus has to land on something a screen reader
// announces by more than its tag.

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
    online,
    onOpenOnItsOwn,
    onShowFullHtml,
    onRegion,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly message: MailThreadMessage;

    /** What marks this message out as the one the conversation arrived at, or `null` where nothing does. */
    readonly mark: ArrivalMark | null;

    /** Whether the deployment is reachable, which decides whether a message the conversation did not carry is read. */
    readonly online: boolean;

    readonly onOpenOnItsOwn: () => void;
    readonly onShowFullHtml: () => void;
    readonly onRegion: (element: HTMLElement | null) => void;
}) {
    const { translate } = useLocalization();
    const email = message.email;
    const sender = email.senderDisplayName ?? email.senderAddress ?? translate('list.senderUnknown');

    // The body the conversation already answered with, which is what makes revealing the history cost no request. The
    // read behind it stays reachable for the asks that are the reader's own — the sender's pictures, the sender's own
    // markup, and reading again after a failure — and it is wanted at all only where there is a message to draw it in.
    const body = useMessageBody(session, transport, email.id, online && message.message !== null, message.body);

    return (
        <li>
            <article
                ref={onRegion}
                tabIndex={-1}
                aria-label={translate('thread.messageBy', { sender })}
                className={`flex flex-col gap-2.75 transition ${mark === null ? '' : arrivalStyles[mark]}`}
            >
                {/* What the mark says, so that the message the conversation arrived at is named rather than only
                    tinted: a rule down an edge is invisible to somebody who is being read to, and the accent is
                    invisible to somebody who cannot tell it from the text beside it. */}
                {mark === null ? null : (
                    <p className="mx-auto flex w-full max-w-conversation items-center">
                        <span className="flex items-center gap-1 rounded-sm bg-accent-soft px-1.75 py-0.5 text-2xs tracking-wide whitespace-nowrap text-accent-deep">
                            <Icon name="arrow_right" className="size-3" />
                            {translate(arrivalLabels[mark])}
                        </span>
                    </p>
                )}

                {message.message === null ? (
                    // A message this deployment could not open is said rather than drawn as an empty card: what the
                    // reader is owed is the correspondence with a gap they can still open on its own.
                    <p className="mx-auto w-full max-w-conversation text-sm text-muted" role="status">
                        {translate('thread.messageNotRead', { sender })}
                    </p>
                ) : (
                    <OpenedMessage
                        session={session}
                        message={message.message}
                        body={body}
                        onShowFullHtml={onShowFullHtml}
                    />
                )}

                <div className="mx-auto flex w-full max-w-conversation flex-col gap-2.75">
                    <p className="text-sm text-muted">
                        {translate('thread.storedIn', { account: email.account, folder: email.folder })}
                    </p>

                    <div>
                        <SecondaryButton label={translate('thread.openOnItsOwn')} onActivate={onOpenOnItsOwn} />
                    </div>
                </div>
            </article>
        </li>
    );
}
