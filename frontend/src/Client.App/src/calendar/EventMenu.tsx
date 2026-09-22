// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';
import { useAgentHandOver } from '../routing/agentHandOver';

// What one event offers, which is this screen's own items handed to the one menu seven lists share: where the menu
// stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s.
//
// ***Ask the agent* hands this event over to the agent**, and is drawn only where this credential has an agent to
// reach: an item the client cannot perform is left out rather than drawn inert, which is the rule the message row's
// own menu states.
//
// **Reminders open the event rather than a surface of their own**, because what edits them is the panel the event's
// own dialog already carries: a second way into the same set would be a second answer to what the event announces.
//
// **Nothing here performs the deletion.** The question it stands behind outlives the menu — the menu is gone the
// moment an item is chosen — so what this does is ask the screen to raise it.

export function EventMenu({
    event,
    at,
    onSelect,
    onOpen,
    onAskReminders,
    onAskDeletion,
    onClose,
}: {
    readonly event: CalendarEvent;
    readonly at: MenuPoint;

    /** Puts this event into the selection the screen holds, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    readonly onOpen: () => void;

    /** Opens this event with its reminders in front, which is the same surface reached with a question already asked. */
    readonly onAskReminders: () => void;

    /** Raises the question the deletion stands behind, over this one event. */
    readonly onAskDeletion: () => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const handToAgent = useAgentHandOver();

    const asking: ContextMenuItem[] =
        handToAgent === null
            ? []
            : [
                  {
                      icon: 'auto_awesome',
                      label: translate('agent.askAgent'),
                      choose: () => {
                          handToAgent({ scope: { kind: 'calendarEvent', subject: event.id }, title: event.title });
                      },
                  },
              ];

    const items: ContextMenuItem[] = [
        { icon: 'check_box', label: translate('calendar.selectEvents'), choose: onSelect },
        { icon: 'event', label: translate('calendar.openEvent'), choose: onOpen },
        { icon: 'notifications', label: translate('calendar.eventReminders'), choose: onAskReminders },
        ...asking,
        { icon: 'delete', label: translate('calendar.deleteEvent'), destroys: true, choose: onAskDeletion },
    ];

    return <ContextMenu header={event.title} at={at} items={items} onClose={onClose} />;
}
