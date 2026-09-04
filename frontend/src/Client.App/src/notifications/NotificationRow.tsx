// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PointerEvent } from 'react';
import type { ClientNotification } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { pressedByFinger, useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { wordNotificationAge } from './notificationAge';
import { notificationKindLabels, notificationTones } from './notificationKinds';

// One thing that happened, as the design project draws it: the kind's own symbol and tint, the headline — heavier and
// darker while it is unread — how long ago it was, what it says, where it came from, the unread mark, and the control
// that changes the read state without opening anything.
//
// **Two controls rather than one row that is a control.** What the row is about and what the toggle does are different
// acts, so each is a button with a name of its own — which is what makes both reachable from a keyboard and both
// assertable by name. A row is never a button with a button inside it.
//
// **How long ago is drawn and when is read.** The relative wording is what somebody scanning the list needs; the
// instant behind it is what the `time` element carries and what a pointer resting on it says, so nothing about the
// row's own wording has to be precise enough to act on.

export function NotificationRow({
    notification,
    selected,
    selecting,
    now,
    onOpen,
    onSelect,
    onToggleRead,
    onPress,
}: {
    readonly notification: ClientNotification;

    readonly selected: boolean;

    /** Whether a selection is being held, which is what makes a plain press pick this row out rather than open it. */
    readonly selecting: boolean;

    /** What the current instant is, which is what the age is measured from and what a test pins. */
    readonly now: number;

    readonly onOpen: () => void;
    readonly onSelect: () => void;
    readonly onToggleRead: () => void;

    /** Opens this row's menu at the point the gesture happened. */
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { locale, translate } = useLocalization();
    const press = useRowPress(onPress);
    const tone = notificationTones[notification.kind];
    const age = wordNotificationAge(notification.occurredAt, locale, now);
    const at = wordInstant(notification.occurredAt, locale, 'full');

    // A modifier held under a pointer is what picks a row out where there is no selection yet, which is the one
    // gesture a finger has no equivalent of — the row's own menu is how it reaches the same thing.
    function pointed(event: PointerEvent<HTMLButtonElement>): void {
        if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
            onSelect();

            return;
        }

        onOpen();
    }

    return (
        <li
            className={`flex items-start gap-3 border-b border-line-soft ps-4 pe-3 ${
                selected ? 'bg-accent-soft' : notification.read ? '' : 'bg-sunken'
            }`}
        >
            <button
                type="button"
                aria-pressed={selecting ? selected : undefined}
                className="flex flex-1 cursor-pointer items-start gap-3 py-3.25 text-start"
                onContextMenu={press.onContextMenu}
                onPointerDown={(event) => {
                    press.onPointerDown(event);

                    // A mouse acts as it goes down; a finger's press is not decided until it is lifted, because the
                    // same touch may become the press that opens this row's menu.
                    if (!pressedByFinger(event.pointerType) && event.button === 0) {
                        pointed(event);
                    }
                }}
                onPointerMove={press.onPointerMove}
                onPointerUp={(event) => {
                    const tapped = pressedByFinger(event.pointerType) && !press.tapSuppressed();

                    press.onPointerUp();

                    if (tapped) {
                        pointed(event);
                    }
                }}
                onPointerCancel={press.onPointerCancel}
                // The keyboard reaches the row through its own activation rather than through a pointer, so the two
                // are separate paths to the same two acts and neither is a simulation of the other.
                onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();

                        if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
                            onSelect();
                        } else {
                            onOpen();
                        }
                    }
                }}
            >
                <span
                    aria-hidden="true"
                    className={`flex size-8 shrink-0 items-center justify-center rounded-xl ${tone.mark}`}
                >
                    <Icon name={tone.icon} className="size-4.75" />
                </span>

                <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                    <span className="flex items-baseline gap-2.25">
                        {/* Unread is weight and colour, which is how the design project draws it, and a word for a
                            reader who is looking at neither. */}
                        <span
                            className={`min-w-0 flex-1 text-md text-pretty ${
                                notification.read ? 'font-medium text-text-soft' : 'font-semibold text-text'
                            }`}
                        >
                            {notification.title}
                        </span>

                        <time
                            dateTime={notification.occurredAt}
                            title={at ?? undefined}
                            className="shrink-0 text-xs whitespace-nowrap text-faint"
                        >
                            {age ?? at}
                        </time>
                    </span>

                    <span className="text-base text-pretty text-text-soft">{notification.body}</span>

                    {/* The kind is what the source line falls back to, because a row says where it came from either
                        way — and the kind is the one thing every notification has. */}
                    <span className="pt-0.25 text-xs text-muted">
                        {notification.source ?? translate(notificationKindLabels[notification.kind])}
                    </span>
                </span>

                {notification.read ? null : <span className="sr-only">{translate('notifications.unreadMark')}</span>}
            </button>

            <span className="flex shrink-0 items-center gap-1.75 py-3.25">
                {notification.read ? null : (
                    <span aria-hidden="true" className="size-2 shrink-0 rounded-full bg-accent" />
                )}

                <button
                    type="button"
                    aria-label={translate(notification.read ? 'notifications.markUnread' : 'notifications.markRead')}
                    title={translate(notification.read ? 'notifications.markUnread' : 'notifications.markRead')}
                    className={`flex size-8 cursor-pointer items-center justify-center rounded-xl border transition pointer-coarse:size-9.5 ${
                        notification.read
                            ? 'border-line bg-panel text-muted'
                            : 'border-transparent bg-accent text-on-accent'
                    }`}
                    onClick={onToggleRead}
                >
                    <Icon name={notification.read ? 'mark_email_unread' : 'mark_email_read'} className="size-4.75" />
                </button>
            </span>
        </li>
    );
}
