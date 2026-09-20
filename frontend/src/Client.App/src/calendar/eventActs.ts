// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';

// What every view does with an event, which is the same four things in all four of them: draw whether it is picked
// out, open it, put it into the selection, and open its own menu. They travel together because a view hands all four
// to every entry it draws and reads none of them itself — the selection is the screen's rather than any one view's.

export interface EventActs {
    /** The events picked out, by identity, which the selection bar names and the acts on it apply to. */
    readonly selected: readonly string[];

    readonly onOpen: (event: CalendarEvent) => void;

    /** Puts one event into the selection or takes it out. */
    readonly onToggle: (event: CalendarEvent) => void;

    /** Opens one event's own menu at the point the gesture happened. */
    readonly onPress: (event: CalendarEvent, at: MenuPoint) => void;
}
