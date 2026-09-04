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
