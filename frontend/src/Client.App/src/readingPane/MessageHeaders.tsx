// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailMessageHeaders, MailParticipant, MailParticipantRole } from '@mailfathom/client-backend';
import { PlannedControl } from '../controls/PlannedControl';
import { ShowFullHtml } from '../fullHtml/ShowFullHtml';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';

// What a message displays above its body: what it is called, who wrote it, when, and everybody else it names. The
// author stands on its own line because it is what a reader checks first, and the rest is a disclosure the platform
// already has an element for — a message addressed to two hundred people would otherwise be a screen of addresses in
// front of the words somebody opened it to read.
//
// Beside the subject stand the three things the design project offers to do with a message from its head. None of them
// exists in the client yet, so each is drawn as what it is: a control the product will have, inert until it does. The
// fourth beside them does exist: the control that opens the sender's own markup on a surface of its own, which is the
// message head's because that is where the design project draws it and because it is a fact about this message rather
// than about the body underneath it.
//
// Every value here is text a sender chose. It is drawn as text and never as markup, so a display name written to look
// like an address, a heading, or a control arrives as the characters it is.

// Every role the disclosure shows, in the order a reader reads them rather than the order the wire lists them.
// `From` is not one of them: the author stands on its own line above, so the disclosure is given everybody else
// and a `From` row here could never be drawn.
type DisclosedRole = Exclude<MailParticipantRole, 'From'>;

const roleOrder: readonly DisclosedRole[] = ['Sender', 'ReplyTo', 'To', 'Cc', 'Bcc'];

const roleLabels: Readonly<Record<DisclosedRole, MessageKey>> = {
    Sender: 'participant.sender',
    ReplyTo: 'participant.replyTo',
    To: 'participant.to',
    Cc: 'participant.cc',
    Bcc: 'participant.bcc',
};

export function MessageHeaders({
    headers,
    onShowFullHtml,
}: {
    readonly headers: MailMessageHeaders;

    /** Opens this message's own markup on the surface that draws it, the reader having confirmed it first. */
    readonly onShowFullHtml: () => void;
}) {
    const { locale, translate } = useLocalization();

    const authors = headers.participants.filter((participant) => participant.role === 'From');
    const others = headers.participants.filter((participant) => participant.role !== 'From');
    const sentAt = wordInstant(headers.sentAt, locale, 'full');
    const receivedAt = wordInstant(headers.receivedAt, locale, 'full');

    return (
        <header className="flex flex-col gap-1.5 border-b border-line px-5.5 py-4">
            <div className="flex flex-wrap items-start gap-x-3 gap-y-1.5">
                <h2 className="min-w-0 flex-1 basis-64 text-3xl font-semibold text-balance">
                    {headers.subject ?? translate('message.noSubject')}
                </h2>

                <div className="flex shrink-0 items-center gap-0.5">
                    <PlannedControl label={translate('mail.reply')} icon="reply" />
                    <PlannedControl label={translate('mail.forward')} icon="forward" />
                    <PlannedControl label={translate('mail.flag')} icon="flag" />
                    <ShowFullHtml onShow={onShowFullHtml} />
                </div>
            </div>

            <p className="text-md font-semibold text-text">
                {authors.length === 0 ? translate('message.noAuthor') : authors.map((one) => named(one)).join(', ')}
            </p>

            {/* Two instants rather than one, because they answer different questions and disagree whenever a message
                sat somewhere: when the author says they wrote it, and when this deployment's last receiving hop
                actually recorded it. Each is placed against the reader's own clock, and each keeps the
                machine-readable form the service sent beside it. */}
            <p className="flex flex-wrap gap-x-3 text-base text-muted">
                {sentAt === null ? (
                    translate('message.sentAtUnknown')
                ) : (
                    <time dateTime={headers.sentAt ?? undefined}>{translate('message.sentAt', { when: sentAt })}</time>
                )}

                {receivedAt === null ? null : (
                    <time dateTime={headers.receivedAt ?? undefined}>
                        {translate('message.receivedAt', { when: receivedAt })}
                    </time>
                )}
            </p>

            {others.length === 0 ? null : <OtherParticipants participants={others} />}
        </header>
    );
}

// A disclosure rather than a list that is always open, and the browser's own rather than one built out of a button and
// a piece of state: it is operable from the keyboard, it announces whether it is open, and it costs no code to be so.
function OtherParticipants({ participants }: { readonly participants: readonly MailParticipant[] }) {
    const { locale, translate } = useLocalization();

    return (
        <details className="text-base">
            <summary className="cursor-pointer text-muted">
                {translate('message.otherParticipants', {
                    count: new Intl.NumberFormat(locale).format(participants.length),
                })}
            </summary>

            <dl className="mt-2 flex flex-col gap-1">
                {roleOrder
                    .map((role) => ({
                        role,
                        addressed: participants.filter((one) => one.role === role),
                    }))
                    .filter((group) => group.addressed.length > 0)
                    .map((group) => (
                        <div key={group.role} className="flex flex-wrap gap-2">
                            <dt className="text-muted">{translate(roleLabels[group.role])}</dt>
                            <dd className="text-text-soft">{group.addressed.map((one) => named(one)).join(', ')}</dd>
                        </div>
                    ))}
            </dl>
        </details>
    );
}

// One address as a person reads it: the name the sender wrote beside the address, and the address itself, because a
// display name is chosen by whoever sent the message and reading only that is how the wrong sender goes unnoticed.
function named(participant: MailParticipant): string {
    return participant.displayName === null
        ? participant.address
        : `${participant.displayName} <${participant.address}>`;
}
