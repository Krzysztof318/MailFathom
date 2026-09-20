// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { KeyboardEvent } from 'react';
import type { CalendarEvent } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { wordInstantRange } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { eventShapes, timeShown, type EventShape } from './eventShapes';

// One event wherever a view draws one, which is its own component for the reason a message row is: it is what carries
// the press, the keyboard path, and a test. Four views draw it and none of them draws a second one, which is what keeps
// picking an event out of a week the same gesture as picking one out of an agenda.
//
// **It answers a press exactly as every other list in this client does**, through `contextMenu/rowPress.ts` rather
// than through a gesture of its own — the design project gives all seven of its lists one behaviour.
//
// **Every entry is its own tab stop**, which is where this list differs from the address book's. That one is a single
// column with a roving tab stop walked by the arrow keys; a week is seven columns and a day is twenty-four rows, so
// there is no one order for an arrow key to walk and a roving stop would leave six of the seven columns unreachable.
// What a view holds is bounded by the span it draws rather than by the mailbox, so tabbing through it is a walk of a
// dozen entries rather than of a hundred thousand.

export function EventEntry({
    event,
    shape,
    selected,
    onOpen,
    onToggle,
    onPress,
}: {
    readonly event: CalendarEvent;

    /** How the view around it draws one, which decides what it has already said. */
    readonly shape: EventShape;

    /** Whether this entry is one of those picked out. */
    readonly selected: boolean;

    readonly onOpen: () => void;

    /** Puts this entry into the selection or takes it out, which a modifier-held press and a plain press both reach. */
    readonly onToggle: () => void;

    /** Opens this entry's own menu at the point the gesture happened. */
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { locale, translate } = useLocalization();
    const press = useRowPress(onPress);
    const look = eventShapes[shape];

    const when = wordInstantRange(event.start, event.end, locale, 'time');

    return (
        <li
            role="option"
            aria-selected={selected}
            tabIndex={0}
            className={`${look.shape} ${selected ? look.selected : look.unselected}`}
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={(clicking) => {
                // The tap that follows a press which has already opened a menu does nothing, or the menu would be
                // closed by the same finger that asked for it.
                if (press.tapSuppressed()) {
                    return;
                }

                if (clicking.ctrlKey || clicking.metaKey || clicking.shiftKey) {
                    onToggle();

                    return;
                }

                onOpen();
            }}
            onKeyDown={(typing: KeyboardEvent) => {
                if (typing.key !== 'Enter' && typing.key !== ' ') {
                    return;
                }

                typing.preventDefault();

                // The same two gestures the pointer has, said with the keyboard: a modifier held picks the entry out,
                // and pressing one plainly while a selection is held picks it out as well — which is the whole of how
                // a selection is worked without a mouse.
                if (typing.ctrlKey || typing.metaKey || typing.shiftKey) {
                    onToggle();

                    return;
                }

                onOpen();
            }}
        >
            {timeShown(shape) && when !== null ? <span className="text-2xs text-muted">{when}</span> : null}

            <span className={`truncate ${shape === 'cell' ? 'text-2xs' : 'text-sm'} text-pretty`}>{event.title}</span>

            {/* The message the event was read out of, said as a mark rather than as a second line: which message it was
                is the event's own surface to say, and a title beside a title would compete with the one the reader is
                scanning for. */}
            {event.sourceMessage === null || shape === 'cell' ? null : (
                <span className="flex items-center gap-1 text-2xs text-accent-deep">
                    <Icon name="open_in_new" className="size-3.5" />
                    {translate('calendar.fromMail')}
                </span>
            )}
        </li>
    );
}
