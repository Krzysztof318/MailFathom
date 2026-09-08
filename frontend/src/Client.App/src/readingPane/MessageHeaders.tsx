// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState, type ReactNode } from 'react';
import type { MailMessageHeaders, MailParticipant, MailParticipantRole } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { BackToList } from '../mailSpace/BackToList';
import { HeadActs, type HeadMessage } from '../mailSpace/HeadActs';
import { useTwoPanes } from '../shell/useWideWorkspace';

// What a message displays above its body: what it is called, who wrote it and when on one line under it, and everybody
// else it names behind a disclosure the platform already has an element for — a message addressed to two hundred
// people would otherwise be a screen of addresses in front of the words somebody opened it to read.
//
// The disclosure is the sender's own line, as the design project draws it: a chevron leads the line, how many others
// there are trails it in the faint tone, and pressing the line unfolds a row per header underneath. The chevron turns
// rather than the line taking a second row for itself, so a head with the disclosure folded is exactly as tall as one
// with nothing to disclose.
//
// One instant rather than two. The head says when the author wrote the message, which is what a reader checks beside
// who wrote it; when this deployment recorded it is a fact about the copy rather than the message, and the message's
// own card carries it beside what else the copy holds. The two disagree whenever a message sat somewhere, and each
// keeps the machine-readable form the service sent beside its wording.
//
// Beside the subject stand the acts the design offers from the head of a message, over the message being read;
// `mailSpace/HeadActs.tsx` says what they are. Opening the sender's own markup is not among them; it is a fact about
// the copy too, and the card carries it beside the instant it was recorded.
//
// Every value here is text a sender chose. It is drawn as text and never as markup, so a display name written to look
// like an address, a heading, or a control arrives as the characters it is.

// Every role the disclosure shows, in the order a reader reads them rather than the order the wire lists them.
// `From` is not one of them: the author stands on the line itself, so the disclosure is given everybody else
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
    message,
}: {
    readonly headers: MailMessageHeaders;

    /** The message the head's acts are about, which is the one being read. */
    readonly message: HeadMessage;
}) {
    const { locale, translate } = useLocalization();
    const twoPanes = useTwoPanes();

    const authors = headers.participants.filter((participant) => participant.role === 'From');
    const others = headers.participants.filter((participant) => participant.role !== 'From');
    const author = authors.length === 0 ? translate('message.noAuthor') : authors.map((one) => named(one)).join(', ');
    const sentAt = wordInstant(headers.sentAt, locale, 'stamp');
    const subject = headers.subject ?? translate('message.noSubject');

    const authorLine = (
        <>
            <span>{author}</span>

            {sentAt === null ? null : (
                <>
                    <span aria-hidden="true">{translate('message.authorThenWhen')}</span>
                    <time dateTime={headers.sentAt ?? undefined}>{sentAt}</time>
                </>
            )}
        </>
    );

    return (
        <header
            className={`flex flex-col border-b border-line ${twoPanes ? 'gap-1.75 px-5.5 py-4' : 'gap-0.5 px-2 pt-1.5 pb-2'}`}
        >
            <div className="flex min-w-0 items-center gap-2.25">
                {twoPanes ? null : <BackToList />}

                <h2
                    className={
                        twoPanes
                            ? 'min-w-0 flex-1 text-3xl font-semibold text-balance'
                            : 'min-w-0 flex-1 truncate text-xl font-semibold'
                    }
                >
                    {subject}
                </h2>

                <HeadActs compact={!twoPanes} message={message} />
            </div>

            {/* A line with nobody behind it is a line rather than a control: a disclosure that unfolds nothing would
                be a chevron promising something the message does not have. */}
            {others.length === 0 ? (
                <p className={`text-muted ${twoPanes ? 'text-base' : 'truncate ps-2 text-sm'}`}>{authorLine}</p>
            ) : (
                <OtherParticipants participants={others} compact={!twoPanes}>
                    {authorLine}
                </OtherParticipants>
            )}

            {sentAt === null ? (
                <p className={`text-sm text-muted ${twoPanes ? '' : 'truncate ps-2'}`}>
                    {translate('message.sentAtUnknown')}
                </p>
            ) : null}
        </header>
    );
}

function OtherParticipants({
    participants,
    compact,
    children,
}: {
    readonly participants: readonly MailParticipant[];
    readonly compact: boolean;
    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();

    // Whether the disclosure is open, read back from the element rather than decided here, because the element is what
    // opens it: the platform toggles it on a press and on a key, and what this changes with it is the sentence the
    // control offers on hover.
    const [open, setOpen] = useState(false);

    return (
        <details
            className="group flex min-w-0 flex-col gap-0.5"
            onToggle={(event) => {
                setOpen(event.currentTarget.open);
            }}
        >
            <summary
                title={translate(open ? 'message.collapseAddressDetails' : 'message.addressDetails')}
                className={`flex max-w-full cursor-pointer items-center gap-1 self-start rounded-md px-1.25 py-px text-muted transition hover:bg-hover hover:text-text-soft [&::-webkit-details-marker]:hidden ${
                    compact ? 'ms-px text-sm' : '-ms-1.25 text-base'
                }`}
            >
                <Icon name="arrow_right" className="size-4.25 shrink-0 text-faint transition group-open:rotate-90" />

                <span className="min-w-0 truncate">{children}</span>

                <span className="shrink-0 text-faint group-open:hidden">
                    <span aria-hidden="true">{translate('message.authorThenWhen')}</span>
                    {translate('message.otherParticipants', {
                        count: new Intl.NumberFormat(locale).format(participants.length),
                    })}
                </span>
            </summary>

            <dl className={`flex flex-col gap-0.75 pt-0.5 pb-0.75 text-sm ${compact ? 'ps-6.75' : 'ps-5.5'}`}>
                {roleOrder
                    .map((role) => ({
                        role,
                        addressed: participants.filter((one) => one.role === role),
                    }))
                    .filter((group) => group.addressed.length > 0)
                    .map((group) => (
                        <div key={group.role} className="flex min-w-0 gap-2.25">
                            <dt className="w-20.5 shrink-0 text-faint">{translate(roleLabels[group.role])}</dt>
                            <dd className="min-w-0 text-pretty text-text-soft">
                                {group.addressed.map((one) => named(one)).join(', ')}
                            </dd>
                        </div>
                    ))}
            </dl>
        </details>
    );
}

function named(participant: MailParticipant): string {
    return participant.displayName === null
        ? participant.address
        : `${participant.displayName} <${participant.address}>`;
}
