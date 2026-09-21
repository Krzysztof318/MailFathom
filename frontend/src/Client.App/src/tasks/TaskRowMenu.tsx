// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PersonalTask } from '@mailfathom/client-backend';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';

// What a task's row offers, which is this list's own items handed to the one menu seven lists share: where the menu
// stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s.
//
// **An item the client cannot yet perform is left out rather than drawn inert**, which is the rule
// `messageRows/MessageRowMenu.tsx` states. Two of the design's six are absent for that reason and each names where it
// arrives: *reminders*, because a task carries none on this surface and
// [#1572](https://github.com/Krzysztof318/MailFathom/issues/1572) is the issue that attaches them — the panel is
// already built and waiting; and *schedule in the calendar* on a deployment that arranges no day, because the act
// behind it is the arrangement rather than a time this client would pick.
//
// **One item the design does not draw here is drawn: taking on what mail proposed.** The design answers a proposal in
// the conversation it came out of, and this screen lists proposals too — so a reader looking at one here would
// otherwise have to go and find the message to accept it. It carries the word the design puts on that act where it
// does draw it.
//
// **Nothing here performs the erasure.** The question it stands behind outlives the menu — the menu is gone the moment
// an item is chosen — so what this does is ask the screen to raise it.

export function TaskRowMenu({
    task,
    at,
    onSelect,
    onToggleCompleted,
    onAccept,
    onSchedule,
    onOpenSource,
    onAskErasure,
    onClose,
}: {
    readonly task: PersonalTask;
    readonly at: MenuPoint;

    /** Puts this row into the selection the list holds, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    readonly onToggleCompleted: () => void;

    /** Takes a task mail proposed on, or `undefined` for one the person already owes. */
    readonly onAccept: (() => void) | undefined;

    readonly onSchedule: () => void;

    /** Opens the message this task cites, or `undefined` where it cites none. */
    readonly onOpenSource: (() => void) | undefined;

    /** Raises the question the erasure stands behind, over this one task. */
    readonly onAskErasure: () => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();

    const items: ContextMenuItem[] = [
        { icon: 'check_box', label: translate('tasks.selectTasks'), choose: onSelect },
        {
            icon: task.completed ? 'radio_button_unchecked' : 'check_circle',
            label: translate(task.completed ? 'tasks.markNotDone' : 'tasks.markDone'),
            choose: onToggleCompleted,
        },
    ];

    if (onAccept !== undefined) {
        items.push({ icon: 'check', label: translate('tasks.accept'), choose: onAccept });
    }

    if (task.dueOn !== null) {
        items.push({ icon: 'calendar_month', label: translate('tasks.scheduleInCalendar'), choose: onSchedule });
    }

    if (onOpenSource !== undefined) {
        items.push({ icon: 'open_in_new', label: translate('tasks.openSourceThread'), choose: onOpenSource });
    }

    items.push({
        icon: 'delete',
        label: translate(task.origin === 'Proposed' ? 'tasks.declineTask' : 'tasks.deleteTask'),
        destroys: true,
        choose: onAskErasure,
    });

    return <ContextMenu header={task.title} at={at} items={items} onClose={onClose} />;
}
