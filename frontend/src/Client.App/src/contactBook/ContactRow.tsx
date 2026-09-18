// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PointerEvent } from 'react';
import type { Contact } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { pressedByFinger, useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { SenderAvatar } from '../controls/SenderAvatar';

// One person on the list, which is its own component for the reason a message row is: it is what carries the press,
// the keyboard path, and a test.
//
// **It answers a press exactly as every other list in this client does**, through `contextMenu/rowPress.ts` rather
// than through a gesture of its own — the design project gives all seven of its lists one behaviour, and a client
// where a contact has to be held a little longer than a message has two gesture vocabularies nobody can learn.
//
// Its height is fixed by the token rather than by its contents, which is load-bearing rather than cosmetic: the window
// the book is drawn through is arithmetic over one height, and a row that grew with a long name would put every row
// below it somewhere other than where the list drew the space for it.
//
// What it draws is the record and nothing derived: the name the book holds and the address to write to. The design
// project draws a company and a last-contact date beside them, and this deployment's contact record carries neither —
// which is a correction owed to the design rather than a field to invent here.

export function ContactRow({
    contact,
    selected,
    open,
    focusable,
    position,
    onOpen,
    onToggle,
    onPress,
    onPoint,
    onElement,
}: {
    readonly contact: Contact;

    /** Whether this row is one of those picked out. */
    readonly selected: boolean;

    /** Whether this is the person the pane beside the list is drawing. */
    readonly open: boolean;

    /** Whether this row is the one the keyboard is on, which is the list's roving tab stop. */
    readonly focusable: boolean;

    /** Where the row sits in the book, counted from one, which is what a reader is told when they land on it. */
    readonly position: number;

    readonly onOpen: () => void;

    /** Puts this row into the selection or takes it out, which a modifier-held press and a plain press both reach. */
    readonly onToggle: () => void;

    /** Opens this row's own menu at the point the gesture happened, or `undefined` where the row offers none. */
    readonly onPress: ((at: MenuPoint) => void) | undefined;

    /** Says the keyboard is now on this row, which a pointer landing on it moves. */
    readonly onPoint: () => void;

    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const press = useRowPress(onPress);

    return (
        <li
            ref={onElement}
            role="option"
            aria-selected={selected}
            aria-posinset={position}
            // The book is keyset-paged, so how many people it holds is not something any page answers. ARIA's unknown
            // size is the accurate answer rather than the number of rows read so far.
            aria-setsize={-1}
            aria-current={open ? 'true' : undefined}
            tabIndex={focusable ? 0 : -1}
            // The mark down the leading edge is the message row's own, and deliberately so: one client draws what is
            // picked out and what is open one way, whichever list is underneath.
            className={`flex h-contact-row cursor-pointer items-center gap-2.75 border-s-4 pe-3.5 ps-2.5 ${
                selected
                    ? 'border-s-accent bg-accent-soft'
                    : open
                      ? 'border-s-accent-strong bg-accent-soft'
                      : 'border-s-transparent bg-panel hover:bg-hover'
            }`}
            onContextMenu={press.onContextMenu}
            onPointerDown={(event: PointerEvent) => {
                press.onPointerDown(event);

                // The primary button alone acts. The second one asks the row what it offers, and a row that also
                // picked the person out under the menu would be answering a question with an act.
                if (!pressedByFinger(event.pointerType) && event.button === 0) {
                    onPoint();
                }
            }}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={(event) => {
                // The tap that follows a press which has already opened a menu does nothing, or the menu would be
                // closed by the same finger that asked for it.
                if (press.tapSuppressed()) {
                    return;
                }

                if (event.ctrlKey || event.metaKey || event.shiftKey) {
                    onToggle();

                    return;
                }

                onOpen();
            }}
        >
            {selected ? (
                <span
                    aria-hidden="true"
                    className="flex size-4.5 shrink-0 items-center justify-center rounded-full bg-accent text-on-accent"
                >
                    <Icon name="check" className="size-3.5" />
                </span>
            ) : null}

            <SenderAvatar displayName={contact.displayName} address={contact.preferredAddress} place="contact" />

            <span className="flex min-w-0 flex-col gap-0.75">
                <span className="truncate text-base font-semibold">{contact.displayName}</span>
                <span className="truncate text-sm text-muted">{contact.preferredAddress}</span>
            </span>
        </li>
    );
}
