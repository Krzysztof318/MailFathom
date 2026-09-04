// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientNotification } from '@mailfathom/client-backend';
import { arrivalCounts } from './arrivalCounts';

// This is where the privacy bound of the desktop head's notification is actually held, so it is asserted as the whole
// answer rather than field by field: what comes back is a kind and a number, and a test written against the whole
// object fails the moment anything a message carried is added to it.

function arrived(kind: ClientNotification['kind'], id: string): ClientNotification {
    return {
        id,
        kind,
        title: 'Ada Lovelace wrote about the engine',
        body: 'The note carried the whole of her answer.',
        source: 'Inbox',
        target: { kind: 'Message', storedEmailId: 'm-9' },
        occurredAt: '2026-09-04T11:55:00+00:00',
        read: false,
    };
}

describe('arrivalCounts', () => {
    it('answers with a kind and a number and nothing a message carried', () => {
        const counted = arrivalCounts([arrived('Mail', 'n-1'), arrived('Mail', 'n-2'), arrived('Task', 'n-3')]);

        expect(counted).toEqual([
            { kind: 'Mail', count: 2 },
            { kind: 'Task', count: 1 },
        ]);
    });

    it('counts each kind in the order it first arrived, so what happened first is said first', () => {
        const counted = arrivalCounts([arrived('Case', 'n-1'), arrived('Mail', 'n-2'), arrived('Case', 'n-3')]);

        expect(counted.map((count) => count.kind)).toEqual(['Case', 'Mail']);
    });

    it('counts nothing where nothing arrived', () => {
        expect(arrivalCounts([])).toEqual([]);
    });
});
