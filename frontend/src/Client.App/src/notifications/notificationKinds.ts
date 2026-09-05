// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { NotificationKind } from '@mailfathom/client-backend';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import type { ToastKind } from '../toasts/useToasts';

// How each of the five kinds is drawn, said, and — when one arrives while somebody is looking at the screen — spoken
// about in the corner. Three lookups rather than three chains inside the components that read them, each exhaustive by
// its own type so a kind the service adds fails to compile until it has been drawn.
//
// The service says which kind a notification is and nothing about how it looks: the symbol, the tint, and the weight a
// toast is raised at are the application's, which is why they are here rather than beside the wire.

/** The symbol and the tint a kind is drawn with, which is the design project's own pairing. */
export interface NotificationTone {
    readonly icon: IconName;

    /** The tint the symbol stands on, and the weight it is drawn at on that tint. */
    readonly mark: string;
}

export const notificationTones: Readonly<Record<NotificationKind, NotificationTone>> = {
    Mail: { icon: 'mail', mark: 'bg-accent-soft text-accent-strong' },
    Calendar: { icon: 'event', mark: 'bg-healthy-soft text-healthy-text' },
    Case: { icon: 'topic', mark: 'bg-warning-soft text-warning-text' },
    Task: { icon: 'task_alt', mark: 'bg-hover text-text-soft' },
    System: { icon: 'sync', mark: 'bg-hover text-muted' },
};

/** What a kind is called where a row's symbol needs saying in words rather than drawing. */
export const notificationKindLabels: Readonly<Record<NotificationKind, MessageKey>> = {
    Mail: 'notifications.kind.mail',
    Calendar: 'notifications.kind.calendar',
    Case: 'notifications.kind.case',
    Task: 'notifications.kind.task',
    System: 'notifications.kind.system',
};

/**
 * How an arrival of one kind is counted where the operating system is told about it, in the forms a language has.
 *
 * A phrase per kind rather than one sentence taking the kind as a hole, which is what
 * `frontend/src/AGENTS.md` § *The two languages* requires of a sentence: in Polish both the count's own form and the
 * adjective in front of the noun follow that noun's gender, so counted messages and counted tasks are two sentences
 * rather than one with a word swapped. English hides that by inflecting nothing.
 *
 * These are the whole of what a system notification may say. Nothing here names a sender, a subject, or anything a
 * message carried — `arrivalCounts.ts` is what makes that true of the value, and this is what makes it true of the
 * words.
 */
export const systemNotificationCounts: Readonly<
    Record<NotificationKind, Readonly<Record<Intl.LDMLPluralRule, MessageKey>>>
> = {
    Mail: counted('mail'),
    Calendar: counted('calendar'),
    Case: counted('case'),
    Task: counted('task'),
    System: counted('system'),
};

/** The four forms one kind is counted in, which every kind declares the same way. */
function counted(
    kind: 'mail' | 'calendar' | 'case' | 'task' | 'system',
): Readonly<Record<Intl.LDMLPluralRule, MessageKey>> {
    return {
        zero: `notifications.arrived.${kind}.other`,
        one: `notifications.arrived.${kind}.one`,
        two: `notifications.arrived.${kind}.other`,
        few: `notifications.arrived.${kind}.few`,
        many: `notifications.arrived.${kind}.many`,
        other: `notifications.arrived.${kind}.other`,
    };
}

/**
 * The weight a toast is raised at when a notification of this kind arrives while the client is open.
 *
 * A case is the one that carries a consequence somebody has to know about without anything having failed, which is
 * what `warning` says; mail and the calendar are the client saying something of its own, and a task and a statement
 * from MailFathom itself take no colour at all.
 */
export const notificationToastKinds: Readonly<Record<NotificationKind, ToastKind>> = {
    Mail: 'info',
    Calendar: 'info',
    Case: 'warning',
    Task: 'neutral',
    System: 'neutral',
};
