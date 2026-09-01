// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PointerEvent, ReactNode } from 'react';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';

// One row of the list, which is its own component for the reason a tree's row is: it is what carries state, a keyboard
// path, and a test. What it draws is what the page answered with — nothing here reads anything of its own, so a row
// costs one request for the whole page it is in.
//
// Its height is fixed by the token rather than by its contents, and that is load-bearing rather than cosmetic: the
// window above it is arithmetic over one height, and a row that grew with a long subject would put every row below it
// somewhere other than where the list drew the space for it.
//
// Everything on it competes for one column narrower than a reading measure, so what it carries is what a person scans
// by: who wrote, when, what about, and how it opens. The sender's host was on the first line and is not, because at
// this width it took the room the name needed and left both of them ellipsised.

export function MessageRow({
    email,
    position,
    open,
    selected,
    focusable,
    onOpen,
    onPoint,
    onPointerEnter,
    onElement,
}: {
    readonly email: MailTimelineEntry;
    readonly position: number;
    readonly open: boolean;
    readonly selected: boolean;
    readonly focusable: boolean;
    readonly onOpen: () => void;
    readonly onPoint: (event: PointerEvent<HTMLLIElement>) => void;
    readonly onPointerEnter: () => void;
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { locale, translate } = useLocalization();

    return (
        <li
            ref={onElement}
            role="option"
            aria-selected={selected}
            aria-posinset={position}
            // The list is keyset-paged, so how many rows the folder holds is not something any page answers. That is
            // what ARIA's unknown size names, and it is the accurate answer rather than the number of rows held.
            aria-setsize={-1}
            aria-current={open ? 'true' : undefined}
            tabIndex={focusable ? 0 : -1}
            onPointerDown={onPoint}
            onPointerEnter={onPointerEnter}
            onDoubleClick={onOpen}
            // Flush and square rather than a card: the rows are one continuous list, each separated from the next by
            // the line it carries, which is what the window's arithmetic needs them to be as well.
            className={`flex h-message-row cursor-pointer flex-col justify-center gap-0.5 overflow-hidden border-b border-line-soft px-3 transition ${
                selected ? 'bg-accent-soft text-accent-strong' : 'text-text-soft hover:bg-hover'
            } ${open ? 'ring-1 ring-inset ring-accent' : ''}`}
        >
            <div className="flex items-baseline gap-2">
                {email.unread ? (
                    <span className="size-2 shrink-0 rounded-full bg-accent">
                        <span className="sr-only">{translate('list.unread')}</span>
                    </span>
                ) : null}

                <span className={`truncate text-sm ${email.unread ? 'font-semibold text-text' : ''}`}>
                    {correspondent(email) ?? translate('list.senderUnknown')}
                </span>

                <Markers email={email} />

                <ReceivedAt at={email.receivedAt} locale={locale} />
            </div>

            <div className="flex items-baseline gap-2 text-sm">
                {/* What the message is about is read before how it opens, so it keeps up to half the line whatever
                    the preview behind it is long enough to ask for, and the preview is what gives way. */}
                <span className={`max-w-1/2 shrink-0 truncate ${email.unread ? 'font-medium text-text' : ''}`}>
                    {email.subject ?? translate('list.noSubject')}
                </span>

                {email.preview === null ? null : <span className="truncate text-faint">{email.preview}</span>}
            </div>
        </li>
    );
}

/** Who the row is about: the sender, else the address it came from, else who it was written to. */
function correspondent(email: MailTimelineEntry): string | undefined {
    return email.senderDisplayName ?? email.senderAddress ?? email.toAddresses[0];
}

// What the mail server said about the message, in the order a reader scans for it. Each carries its own words, because
// a mark with no name is a mark nobody using a screen reader can see at all.
function Markers({ email }: { readonly email: MailTimelineEntry }) {
    const { translate } = useLocalization();

    return (
        <span className="ms-auto flex shrink-0 items-center gap-1">
            {email.answered ? (
                <Marker label={translate('list.answered')}>
                    <path d="M10 9V5l-7 7 7 7v-4.1c5 0 8.5 1.6 11 5.1-1-5-4-10-11-11Z" />
                </Marker>
            ) : null}

            {email.hasAttachments ? (
                <Marker label={translate('list.attachments', { count: String(email.attachmentCount) })}>
                    <path d="M16.5 6.5v9a4.5 4.5 0 1 1-9 0V5.5a3 3 0 1 1 6 0v9a1.5 1.5 0 1 1-3 0v-8H9v8a3 3 0 1 0 6 0v-9a4.5 4.5 0 1 0-9 0v10a6 6 0 0 0 12 0v-9h-1.5Z" />
                </Marker>
            ) : null}

            {email.flagged ? (
                <Marker label={translate('list.flagged')}>
                    <path d="m12 17.3-6.2 3.7 1.7-7L2 9.2l7.2-.6L12 2l2.8 6.6 7.2.6-5.5 4.8 1.7 7L12 17.3Z" />
                </Marker>
            ) : null}
        </span>
    );
}

function Marker({ label, children }: { readonly label: string; readonly children: ReactNode }) {
    return (
        <span className="text-muted">
            <svg viewBox="0 0 24 24" aria-hidden="true" className="size-3.5 fill-current">
                {children}
            </svg>
            <span className="sr-only">{label}</span>
        </span>
    );
}

// When the last receiving hop recorded the message, formatted by `Intl` under the active locale rather than assembled
// here. The machine-readable form stays on the element beside it, which is what lets anything reading the document work
// with the instant rather than with somebody's local spelling of it.
function ReceivedAt({ at, locale }: { readonly at: string | null; readonly locale: string }) {
    if (at === null) {
        return null;
    }

    const received = new Date(at);

    if (Number.isNaN(received.getTime())) {
        return null;
    }

    const when = new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' });

    return (
        <time dateTime={at} className="shrink-0 text-xs tabular-nums text-faint">
            {when.format(received)}
        </time>
    );
}
