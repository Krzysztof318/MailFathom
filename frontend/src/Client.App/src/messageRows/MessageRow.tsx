// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PointerEvent, ReactNode } from 'react';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { ReceivedAt } from '../controls/ReceivedAt';
import { useLocalization } from '../localization/useLocalization';

// One row of mail, which is its own component for the reason a tree's row is: it is what carries state, a keyboard
// path, and a test. What it draws is what the page answered with — nothing here reads anything of its own, so a row
// costs one request for the whole page it is in.
//
// It sits beside the arithmetic that decides which rows are in the document rather than inside either screen drawing
// it, because two of them now do: the folder's list and the search's results are one row read two ways, and a second
// arrangement of the same three lines is how the client would stop looking like one product.
//
// Its height is fixed by the token rather than by its contents, and that is load-bearing rather than cosmetic: the
// window above it is arithmetic over one height, and a row that grew with a long subject would put every row below it
// somewhere other than where the list drew the space for it. The third line is what that height reserves for a
// sentence about the message rather than from it — why a search result is in the list today, and what MailFathom made
// of the message when stage 3 lands. A row given none keeps the space, so the row that gains one is this row rather
// than a taller one.

export function MessageRow({
    email,
    position,
    open,
    selected,
    focusable,
    note,
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

    /** What the row has to say about the message beyond what it draws, in the line the height already reserves. */
    readonly note?: ReactNode;
    readonly onOpen: () => void;
    readonly onPoint: (event: PointerEvent<HTMLLIElement>) => void;
    readonly onPointerEnter: () => void;
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { translate } = useLocalization();

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

                {/* Who wrote is read before where they wrote from, so the name keeps up to half the line and the host
                    is what gives way. Half rather than more because the marks and the time hold their own width: a
                    name allowed past it would push the time out of a row that clips rather than wraps. */}
                <span
                    className={`max-w-1/2 shrink-0 truncate text-sm ${email.unread ? 'font-semibold text-text' : ''}`}
                >
                    {correspondent(email) ?? translate('list.senderUnknown')}
                </span>

                <Organisation address={email.senderAddress} />

                <Markers email={email} />

                <ReceivedAt at={email.receivedAt} />
            </div>

            <div className="flex items-baseline gap-2 text-sm">
                {/* What the message is about is read before how it opens, so it keeps up to half the line whatever
                    the preview behind it is long enough to ask for, and the preview is what gives way. */}
                <span className={`max-w-1/2 shrink-0 truncate ${email.unread ? 'font-medium text-text' : ''}`}>
                    {email.subject ?? translate('list.noSubject')}
                </span>

                {email.preview === null ? null : <span className="truncate text-faint">{email.preview}</span>}
            </div>

            {/* The reserved line. Hidden from the accessibility tree where it holds nothing, so a row with nothing
                to say about itself is not announced as one with an empty line in it. */}
            <div aria-hidden={note === undefined ? 'true' : undefined} className="h-4 overflow-hidden text-xs">
                {note}
            </div>
        </li>
    );
}

/** Who the row is about: the sender, else the address it came from, else who it was written to. */
function correspondent(email: MailTimelineEntry): string | undefined {
    return email.senderDisplayName ?? email.senderAddress ?? email.toAddresses[0];
}

// The host the message came from, which is what the reader recognises when the display name is somebody's first name
// and the address is not shown. Absent where the sender wrote no address, rather than drawn as an empty parenthesis.
// It gives way before the name does: a column this narrow cannot hold both in full, and the name is what is scanned.
function Organisation({ address }: { readonly address: string | null }) {
    const at = address?.lastIndexOf('@') ?? -1;

    if (address === null || at < 0 || at === address.length - 1) {
        return null;
    }

    return <span className="hidden truncate text-xs text-faint workspace:inline">{address.slice(at + 1)}</span>;
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
