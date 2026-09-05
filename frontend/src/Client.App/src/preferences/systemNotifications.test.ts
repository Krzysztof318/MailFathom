// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { deviceKeys } from '../device/deviceStore';
import { chooseSystemNotifications, systemNotificationsChosen } from './systemNotifications';

// One decision is worth pinning down here and it is the default: a machine nobody has answered for raises them, and
// only an answer of `false` — a person moving the switch, or an operating system refusing — stops it. A value written
// by an older client, or by something else on the origin, reads as the answer it is not rather than as an off.

afterEach(() => {
    window.localStorage.removeItem(deviceKeys.systemNotifications);
});

describe('systemNotificationsChosen', () => {
    it('raises them on a machine nobody has answered for', () => {
        expect(systemNotificationsChosen()).toBe(true);
    });

    it('honours the refusal a person or an operating system wrote, across a restart of the client', () => {
        chooseSystemNotifications(false);

        expect(window.localStorage.getItem(deviceKeys.systemNotifications)).toBe('false');
        expect(systemNotificationsChosen()).toBe(false);
    });

    it('raises them again once the machine is told to', () => {
        chooseSystemNotifications(false);
        chooseSystemNotifications(true);

        expect(systemNotificationsChosen()).toBe(true);
    });
});
