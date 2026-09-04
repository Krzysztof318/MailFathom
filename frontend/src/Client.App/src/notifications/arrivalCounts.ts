// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientNotification, NotificationKind } from '@mailfathom/client-backend';

// What an arrival is reduced to before anything outside this client is told about it: how many, and of what kind.
//
// It is a module of its own rather than three lines inside the hook that raises the notification, because this is where
// the privacy bound of #1609 is actually enforced and where it can be asserted without a frame around it. A system
// notification lands in the operating system's action centre and on its lock screen — storage MailFathom cannot retain,
// redact, or erase — so the title, the body, the source, and the target of every arrival stop here, and what leaves is
// a kind and a number. Anything a caller wanted to add is a caller reaching past this function rather than a shortfall
// in it, which is exactly the shape a test can hold.
//
// The kinds come back in the order they first arrived rather than in the order the service declares them, so what a
// reader is told first is what happened first.

/** How many notifications of one kind arrived together. */
export interface ArrivalCount {
    readonly kind: NotificationKind;

    /** How many arrived, which is at least one — a kind with nothing behind it is absent rather than counted at zero. */
    readonly count: number;
}

/** Reduces an arrival to the counts a system notification may carry, and to nothing else. */
export function arrivalCounts(arrived: readonly ClientNotification[]): readonly ArrivalCount[] {
    const counted = new Map<NotificationKind, number>();

    for (const notification of arrived) {
        counted.set(notification.kind, (counted.get(notification.kind) ?? 0) + 1);
    }

    return [...counted].map(([kind, count]) => ({ kind, count }));
}
