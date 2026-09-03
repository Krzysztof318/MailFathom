// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { deliveryFailureOf } from './attachmentDelivery';

// What a download becomes in the record `Client.Backend` keeps of it. It is asserted for every one of the six outcomes
// rather than for the interesting ones, because this reading is what an operator's own dimension is built from: an
// outcome mapped to the wrong reason is a dashboard that says a deployment is refusing downloads it delivered.

describe('deliveryFailureOf', () => {
    it.each(['delivered', 'abandoned'] as const)('reports %s as an answer the client acted on', (outcome) => {
        expect(deliveryFailureOf(outcome)).toBeNull();
    });

    it('reports a file larger than the message described as a body this client refused', () => {
        expect(deliveryFailureOf('largerThanDescribed')).toBe('unreadable');
    });

    it.each(['unauthenticated', 'unauthorized', 'unavailable'] as const)(
        'reports %s as the failure that word already names on this surface',
        (outcome) => {
            expect(deliveryFailureOf(outcome)).toBe(outcome);
        },
    );
});
