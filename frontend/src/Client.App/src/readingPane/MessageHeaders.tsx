// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailMessageHeaders, MailParticipant, MailParticipantRole } from '@mailfathom/client-backend';
import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { BackToList } from '../mailSpace/BackToList';
import { useTwoPanes } from '../shell/useWideWorkspace';

// What a message displays above its body: what it is called, who wrote it and when on one line under it, and everybody
// else it names behind a disclosure the platform already has an element for — a message addressed to two hundred
// people would otherwise be a screen of addresses in front of the words somebody opened it to read.
//
// One instant rather than two. The head says when the author wrote the message, which is what a reader checks beside
// who wrote it; when this deployment recorded it is a fact about the copy rather than the message, and the message's
// own card carries it beside what else the copy holds. The two disagree whenever a message sat somewhere, and each
// keeps the machine-readable form the service sent beside its wording.
//
// Beside the subject stand the three things the design project offers to do with a message from its head, as words
// alone: the design draws them without symbols wherever the head has a column to itself, and as symbols alone where
// the column is the whole screen and the head is one compact bar over the message — the way back to the list at its
// start, the subject on one line, the acts at its end. None of the three exists in the client yet, so each is drawn as
// what it is: a control the product will have, inert until it does. Opening the sender's own markup is not among them;
// it is a fact about the copy too, and the card carries it beside the instant it was recorded.
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

export function MessageHeaders({ headers }: { readonly headers: MailMessageHeaders }) {
    const { locale, translate } = useLocalization();
    const twoPanes = useTwoPanes();
    const actShape: ControlShape = twoPanes ? 'named' : 'symbol';

    const authors = headers.participants.filter((participant) => participant.role === 'From');
    const others = headers.participants.filter((participant) => participant.role !== 'From');
    const author = authors.length === 0 ? translate('message.noAuthor') : authors.map((one) => named(one)).join(', ');
    const sentAt = wordInstant(headers.sentAt, locale, 'stamp');
    const subject = headers.subject ?? translate('message.noSubject');

    return (
        <header
            className={`flex flex-col border-b border-line ${twoPanes ? 'gap-1.75 px-5.5 py-4' : 'gap-1 px-2 py-2'}`}
        >
            <div className="flex items-center gap-1">
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

                <div className="flex shrink-0 items-center gap-0.5">
                    <PlannedControl label={translate('mail.reply')} icon="reply" shape={actShape} />
                    <PlannedControl label={translate('mail.forward')} icon="forward" shape={actShape} />
                    <PlannedControl label={translate('mail.flag')} icon="flag" shape={actShape} />
                </div>
            </div>

            <p className={`text-base text-muted ${twoPanes ? '' : 'truncate ps-2'}`}>
                <span>{author}</span>

                {sentAt === null ? null : (
                    <>
                        <span aria-hidden="true">{translate('message.authorThenWhen')}</span>
                        <time dateTime={headers.sentAt ?? undefined}>{sentAt}</time>
                    </>
                )}
            </p>

            {sentAt === null ? (
                <p className={`text-sm text-muted ${twoPanes ? '' : 'truncate ps-2'}`}>
                    {translate('message.sentAtUnknown')}
                </p>
            ) : null}

            {others.length === 0 ? null : (
                <div className={twoPanes ? '' : 'ps-2'}>
                    <OtherParticipants participants={others} />
                </div>
            )}
        </header>
    );
}

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

function named(participant: MailParticipant): string {
    return participant.displayName === null
        ? participant.address
        : `${participant.displayName} <${participant.address}>`;
}
