// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it, vi } from 'vitest';
import { followTarget, type NotificationDestinations } from './notificationDestination';

function following(): NotificationDestinations {
    return { openMail: vi.fn(), goTo: vi.fn() };
}

describe('followTarget', () => {
    it('opens the message a notification about mail names', () => {
        const client = following();

        followTarget({ kind: 'Message', storedEmailId: 'e-9' }, client);

        expect(client.openMail).toHaveBeenCalledExactlyOnceWith('e-9');
        expect(client.goTo).not.toHaveBeenCalled();
    });

    it('goes to the space a notification about a screen names', () => {
        const client = following();

        followTarget({ kind: 'Screen', screen: 'Mail' }, client);

        expect(client.goTo).toHaveBeenCalledExactlyOnceWith('mail');
        expect(client.openMail).not.toHaveBeenCalled();
    });

    it('leaves the reader where they were where the screen has no address in this client yet', () => {
        const client = following();

        followTarget({ kind: 'Screen', screen: 'Settings' }, client);

        expect(client.goTo).not.toHaveBeenCalled();
        expect(client.openMail).not.toHaveBeenCalled();
    });

    it('leaves the reader where they were where the notification was never a place to go', () => {
        const client = following();

        followTarget({ kind: 'Nothing' }, client);

        expect(client.goTo).not.toHaveBeenCalled();
        expect(client.openMail).not.toHaveBeenCalled();
    });
});
