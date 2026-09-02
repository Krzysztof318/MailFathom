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

// One message of a conversation, collapsed to a line or opened to what it says. It is its own component for the reason
// a list row is: it is what carries the expansion, the keyboard path, and the read.
//
// The read is the point. A conversation of thirty messages is thirty bodies nobody asked for, which is the defect the
// withdrawn client carried here — every row built its whole expanded body before anybody expanded it, on every render.
// So `Message` is mounted by the expansion rather than hidden by it: a collapsed message costs one line of markup and
// no request at all, and the request it does cost is made when somebody opens it.
//
// The disclosure is the browser's own, for the reason `readingPane/MessageHeaders.tsx` gives: it is reached by the
// keyboard in document order, it is operated by Enter and by Space without anything here handling a key, and it
// announces whether it is open. What is added to it is that this one is controlled — a conversation decides which of
// its messages open when it is first drawn, and after that the reader does.
//
// It is drawn as the card the design project draws a message of a conversation as: flat on the surface while it is a
// line, and raised on the panel once it is open.

export function ThreadMessage({
    session,
    transport,
    message,
    expanded,
    onExpanded,
    onOpenOnItsOwn,
    onSummary,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly message: MailThreadMessage;
    readonly expanded: boolean;
    readonly onExpanded: (expanded: boolean) => void;
    readonly onOpenOnItsOwn: () => void;
    readonly onSummary: (element: HTMLElement | null) => void;
}) {
    const { translate } = useLocalization();
    const email = message.email;

    return (
        <li>
            <details
                className="overflow-hidden rounded-2xl border border-line bg-sunken open:bg-panel open:shadow-raised"
                open={expanded}
                onToggle={(event) => {
                    onExpanded(event.currentTarget.open);
                }}
            >
                <summary
                    ref={onSummary}
                    className="flex cursor-pointer items-center gap-2.5 px-3.75 py-3 transition hover:bg-hover"
                >
                    <SenderAvatar displayName={email.senderDisplayName} address={email.senderAddress} place="card" />

                    {email.unread ? (
                        <span className="size-2 shrink-0 rounded-full bg-accent">
                            <span className="sr-only">{translate('list.unread')}</span>
                        </span>
                    ) : null}

                    {/* Who wrote is what a conversation is scanned by, so it keeps its width and the contribution
                        behind it is what gives way. */}
                    <span className={`shrink-0 text-md font-semibold ${email.unread ? 'text-text' : 'text-text-soft'}`}>
                        {email.senderDisplayName ?? email.senderAddress ?? translate('list.senderUnknown')}
                    </span>

                    <Organisation address={email.senderAddress} />

                    {/* What this message added, trimmed of the history it quoted by the deployment rather than here.
                        It gives way to the body once the message is open, where drawing it again would be the same
                        words twice on one screen. */}
                    {expanded ? null : (
                        <span className="truncate text-sm text-muted">
                            {email.preview ?? translate('thread.contributionNotExtracted')}
                        </span>
                    )}

                    <MessageMarkers email={email} />

                    <ReceivedAt at={email.receivedAt} />
                </summary>

                {/* Mounted by the expansion rather than hidden by it, which is what makes a collapsed message cost no
                    body read. Nothing below is drawn — and nothing is asked of the deployment — until it is open. */}
                {expanded ? (
                    <div className="flex flex-col gap-3 px-3.75 pt-1 pb-3.75">
                        <p className="text-sm text-muted">
                            {translate('thread.storedIn', { account: email.account, folder: email.folder })}
                        </p>

                        <Message
                            session={session}
                            transport={transport}
                            storedEmailId={email.id}
                            quotedHistoryOnRequest
                        />

                        <div>
                            <SecondaryButton label={translate('thread.openOnItsOwn')} onActivate={onOpenOnItsOwn} />
                        </div>
                    </div>
                ) : null}
            </details>
        </li>
    );
}
