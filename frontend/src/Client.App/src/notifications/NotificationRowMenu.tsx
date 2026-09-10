// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientNotification } from '@mailfathom/client-backend';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';

// What a notification row answers a press with. It is this row's items and nothing else: where the menu stands, how it
// is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s, which is the same component six other lists open.
//
// **Picking the row out is first**, because it is the one act a finger has no other route to — there is no modifier
// key to hold on a touch screen, so the menu is where a selection starts.
//
// **An item the client cannot yet perform is left out rather than drawn inert**, which is the rule the message row's
// own menu states. One item the design project draws is absent under it: *open the source*, on a notification that
// names no target, because there is nothing to open.
//
// **Deleting is last and drawn as what it is.** It takes a row out of the centre for good, so it sits apart from the
// three acts above it and the question in front of it is the panel's rather than this menu's — a menu that closes as it
// is chosen from cannot also hold a dialog open.

export function NotificationRowMenu({
    notification,
    at,
    onSelect,
    onToggleRead,
    onOpen,
    onDelete,
    onClose,
}: {
    readonly notification: ClientNotification;
    readonly at: MenuPoint;

    /** Puts this row into the panel's own selection, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    readonly onToggleRead: () => void;

    /** Reads it and goes where it leads, which is what the row's own press does. */
    readonly onOpen: () => void;

    /** Asks the panel to put the question in front of removing this row for good. */
    readonly onDelete: () => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();

    const items: readonly ContextMenuItem[] = [
        { icon: 'check_box', label: translate('notifications.select'), choose: onSelect },
        {
            icon: notification.read ? 'mark_email_unread' : 'mark_email_read',
            label: translate(notification.read ? 'notifications.markUnread' : 'notifications.markRead'),
            choose: onToggleRead,
        },
        ...(notification.target.kind === 'Nothing'
            ? []
            : [{ icon: 'open_in_new' as const, label: translate('notifications.openSource'), choose: onOpen }]),
        { icon: 'delete', label: translate('notifications.delete'), destroys: true, choose: onDelete },
    ];

    return <ContextMenu header={notification.title} at={at} items={items} onClose={onClose} />;
}
