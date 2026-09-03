// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PointerEvent, ReactNode } from 'react';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { MessageMarkers } from '../controls/MessageMarkers';
import { Organisation } from '../controls/Organisation';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SenderAvatar } from '../controls/SenderAvatar';
import { useLocalization } from '../localization/useLocalization';
import { drawnUnread, useReadMarking } from '../readMarking/useReadMarking';

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
// somewhere other than where the list drew the space for it. The three lines are the design project's: who wrote and
// when, what about, and a line for a sentence about the message rather than from it — why a search result is in the
// list today, and what MailFathom made of the message when stage 3 lands. A row given none keeps the space, so the row
// that gains one is this row rather than a taller one.

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
    const marking = useReadMarking();

    // What the deployment last reported, less what this client has marked read since. The two are not the same for
    // minutes at a time: marking read is a durable mutation the account's own pass carries to the mail server, and the
    // stored flag is an observation of what that server was seen to hold — so the row draws from the pending mutation
    // rather than waiting for the observation to catch up.
    //
    // The row stays in the list either way, including a list narrowed to unread mail. A message drawn read is a message
    // the person is reading; taking its row out from under them the moment the pane rendered it would be the list
    // disagreeing with the pane about what is open, and the next read of the folder is what removes it.
    const unread = drawnUnread(marking, email.id, email.unread);

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
            // the line it carries, which is what the window's arithmetic needs them to be as well. The row that is
            // open, and the rows picked out for a question, are marked at the edge rather than by a ring around them,
            // which is the design project's mark and keeps the row's own lines where they were.
            className={`flex h-message-row cursor-pointer flex-col justify-center gap-0.75 overflow-hidden border-b border-s-4 border-b-sunken ps-2.5 pe-3.5 transition ${
                selected
                    ? 'border-s-accent bg-accent-soft'
                    : open
                      ? 'border-s-accent-line bg-accent-soft'
                      : 'border-s-transparent hover:bg-hover'
            }`}
        >
            <div className="flex items-center gap-2">
                {/* Unread is drawn as weight and colour, which is how the design project draws it, and said in as many
                    words for a reader who is not looking at either. Nothing marks the row visually beside that: a dot
                    ahead of the avatar would inset every unread row a little further than every read one, which is the
                    one thing a list of rows that have to scan as a column cannot afford. */}
                {unread ? <span className="sr-only">{translate('list.unread')}</span> : null}

                <SenderAvatar displayName={email.senderDisplayName} address={email.senderAddress} place="row" />

                {/* Who wrote is read before where they wrote from, so the name keeps up to half the line and the host
                    is what gives way. Half rather than more because the marks and the time hold their own width: a
                    name allowed past it would push the time out of a row that clips rather than wraps. */}
                <span
                    className={`max-w-1/2 shrink-0 truncate text-md font-semibold ${unread ? 'text-text' : 'text-text-soft'}`}
                >
                    {correspondent(email) ?? translate('list.senderUnknown')}
                </span>

                <Organisation address={email.senderAddress} />

                <MessageMarkers email={email} />

                <ReceivedAt at={email.receivedAt} />
            </div>

            <div className={`truncate text-md ${unread ? 'text-text' : 'text-text-soft'}`}>
                {email.subject ?? translate('list.noSubject')}
            </div>

            {/* The reserved line. Hidden from the accessibility tree where it holds nothing, so a row with nothing
                to say about itself is not announced as one with an empty line in it. */}
            <div
                aria-hidden={note === undefined ? 'true' : undefined}
                className="h-4 overflow-hidden text-xs text-muted"
            >
                {note}
            </div>
        </li>
    );
}

/** Who the row is about: the sender, else the address it came from, else who it was written to. */
function correspondent(email: MailTimelineEntry): string | undefined {
    return email.senderDisplayName ?? email.senderAddress ?? email.toAddresses[0];
}
