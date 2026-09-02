// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientSession, MailFathomTransport, MailThreadMessage } from '@mailfathom/client-backend';
import { MessageMarkers } from '../controls/MessageMarkers';
import { Organisation } from '../controls/Organisation';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SenderAvatar } from '../controls/SenderAvatar';
import { useLocalization } from '../localization/useLocalization';
import { Message } from '../messageBody/Message';

// One message of a conversation, as a head and what it says. It is its own component for the reason a list row is: it
// is what carries the read and the way out to the message on its own.
//
// A message the pane draws is open, which is what the design project draws and what leaves the pane with one decision
// rather than one per message. The economy that used to sit here sits on the conversation instead: the history is
// hidden until somebody asks for it, so a conversation of thirty messages mounts one body rather than thirty. Nothing
// here collapses, so there is no disclosure, no contribution line, and no card — the border and the panel fill belong
// to a collapsed message, which this pane no longer has.
//
// It is a region of its own so that arriving at a conversation can place the reader on the message they came for, for
// the reason `readingPane/ReadingPane.tsx` names its opened message: focus has to land on something a screen reader
// announces by more than its tag.

export function ThreadMessage({
    session,
    transport,
    message,
    onOpenOnItsOwn,
    onRegion,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly message: MailThreadMessage;
    readonly onOpenOnItsOwn: () => void;
    readonly onRegion: (element: HTMLElement | null) => void;
}) {
    const { translate } = useLocalization();
    const email = message.email;
    const sender = email.senderDisplayName ?? email.senderAddress ?? translate('list.senderUnknown');

    return (
        <li>
            <article
                ref={onRegion}
                tabIndex={-1}
                aria-label={translate('thread.messageBy', { sender })}
                className="flex flex-col gap-2.75"
            >
                <div className="flex items-center gap-2.75">
                    <SenderAvatar displayName={email.senderDisplayName} address={email.senderAddress} place="card" />

                    {email.unread ? (
                        <span className="size-2 shrink-0 rounded-full bg-accent">
                            <span className="sr-only">{translate('list.unread')}</span>
                        </span>
                    ) : null}

                    {/* Who wrote is what a conversation is scanned by, so it keeps its width and everything beside it
                        is what gives way. */}
                    <span className={`shrink-0 text-md font-semibold ${email.unread ? 'text-text' : 'text-text-soft'}`}>
                        {sender}
                    </span>

                    <Organisation address={email.senderAddress} />

                    <MessageMarkers email={email} />

                    <ReceivedAt at={email.receivedAt} />
                </div>

                <p className="text-sm text-muted">
                    {translate('thread.storedIn', { account: email.account, folder: email.folder })}
                </p>

                <Message session={session} transport={transport} storedEmailId={email.id} quotedHistoryOnRequest />

                <div>
                    <SecondaryButton label={translate('thread.openOnItsOwn')} onActivate={onOpenOnItsOwn} />
                </div>
            </article>
        </li>
    );
}
