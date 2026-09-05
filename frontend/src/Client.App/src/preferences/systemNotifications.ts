// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useSyncExternalStore } from 'react';
import { deviceKeys, deviceStore } from '../device/deviceStore';

// Whether this machine raises a notification of its own while nobody is looking at the window.
//
// It is kept on the device rather than by the deployment, and that is a decision rather than a convenience: a
// notification is raised by one operating system on one machine, so somebody who wants them on the laptop and not on
// the machine in the office is describing two machines rather than changing their mind. `frontend/src/AGENTS.md`
// § *State* is the rule that would otherwise put a setting chosen after signing in on the deployment, and this is the
// case that rule's own reasoning excludes.
//
// It is one value for the machine rather than one per person, for the same reason the theme is: what it decides is
// whether this operating system is spoken to at all, and a notification says a count and a kind rather than anything
// about whose mail it counted. Nothing under this key can be read back as a statement about a person.
//
// Unset reads as on, which is the default the operating system's own grant makes true — and a refusal is written here
// as the permanent off, so a machine that said no is never asked again by a client that had forgotten.
//
// The store is the one owner of the value and a screen never keeps a second copy, because the person moving the switch
// is not the only writer: an arrival the operating system refuses writes the same key from `useNotificationCentre.ts`,
// and a switch holding its own copy would still read *on* while the machine had already decided otherwise.
// `useSyncExternalStore` is what React reads a value living outside it through, so what is subscribed to below is the
// write rather than the storage — the same document made it, which is exactly what the `storage` event does not report.

/** Whether this machine may raise a system notification, which nothing has said `false` about until something does. */
export function systemNotificationsChosen(): boolean {
    return deviceStore().read(deviceKeys.systemNotifications) !== 'false';
}

/** Keeps what was chosen on this machine, whether by a person moving the switch or by the operating system refusing. */
export function chooseSystemNotifications(raising: boolean): void {
    deviceStore().write(deviceKeys.systemNotifications, String(raising));

    for (const told of watching) {
        told();
    }
}

const watching = new Set<() => void>();

function watchSystemNotifications(changed: () => void): () => void {
    watching.add(changed);

    return () => {
        watching.delete(changed);
    };
}

/** What this machine has chosen, kept current for as long as a screen is drawing it. */
export function useSystemNotificationsChosen(): boolean {
    return useSyncExternalStore(watchSystemNotifications, systemNotificationsChosen);
}
