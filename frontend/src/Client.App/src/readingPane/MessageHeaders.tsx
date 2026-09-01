// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailMessageHeaders, MailParticipant, MailParticipantRole } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import { useLocalization } from '../localization/useLocalization';

// What a message displays above its body: what it is called, who wrote it, when, and everybody else it names. The
// author stands on its own line because it is what a reader checks first, and the rest is a disclosure the platform
// already has an element for — a message addressed to two hundred people would otherwise be a screen of addresses in
// front of the words somebody opened it to read.
//
// Every value here is text a sender chose. It is drawn as text and never as markup, so a display name written to look
// like an address, a heading, or a control arrives as the characters it is.

// Every role the service publishes, in the order a reader reads them rather than the order the wire lists them.
const roleOrder: readonly MailParticipantRole[] = ['From', 'Sender', 'ReplyTo', 'To', 'Cc', 'Bcc'];

const roleLabels: Readonly<Record<MailParticipantRole, MessageKey>> = {
    From: 'participant.from',
    Sender: 'participant.sender',
    ReplyTo: 'participant.replyTo',
    To: 'participant.to',
    Cc: 'participant.cc',
    Bcc: 'participant.bcc',
};

export function MessageHeaders({ headers }: { readonly headers: MailMessageHeaders }) {
    const { locale, translate } = useLocalization();

    const authors = headers.participants.filter((participant) => participant.role === 'From');
    const others = headers.participants.filter((participant) => participant.role !== 'From');
    const sentAt = instantOf(headers.sentAt, locale);

    return (
        <header className="flex flex-col gap-2 border-b border-line-soft pb-4">
            <h2 className="text-xl font-semibold tracking-tight">
                {headers.subject ?? translate('message.noSubject')}
            </h2>

            <p className="text-sm text-text-soft">
                {authors.length === 0
                    ? translate('message.noAuthor')
                    : authors.map((author) => named(author)).join(', ')}
            </p>

            <p className="text-sm text-muted">
                {sentAt === null ? translate('message.sentAtUnknown') : translate('message.sentAt', { when: sentAt })}
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
        <details className="text-sm">
            <summary className="cursor-pointer text-muted">
                {translate('message.otherParticipants', {
                    count: new Intl.NumberFormat(locale).format(participants.length),
                })}
            </summary>

            <dl className="mt-2 flex flex-col gap-1">
                {roleOrder
                    .map((role) => ({ role, addressed: participants.filter((one) => one.role === role) }))
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

// The instant as the platform words it under the active language, rather than as anything a catalogue holds. A date the
// sender wrote that this client cannot read is an absence rather than a value to repair.
function instantOf(instant: string | null, locale: Locale): string | null {
    if (instant === null) {
        return null;
    }

    const at = Date.parse(instant);

    return Number.isNaN(at)
        ? null
        : new Intl.DateTimeFormat(locale, { dateStyle: 'long', timeStyle: 'short' }).format(at);
}
