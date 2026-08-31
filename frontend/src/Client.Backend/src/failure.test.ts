// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { failed, failureReasonForStatus, read } from './failure';

describe('failureReasonForStatus', () => {
    // The four reasons exist because a screen acts differently on each, so the mapping is asserted status by status
    // rather than as "not read": a refused credential that arrived as `unavailable` would be retried forever instead
    // of sending the person back to sign in.
    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
        [503, 'unavailable'],
    ])('reads %i as %s', (status, reason) => {
        expect(failureReasonForStatus(status)).toBe(reason);
    });
});

describe('ClientResult', () => {
    it('carries the value it read', () => {
        expect(read('answer')).toEqual({ outcome: 'read', value: 'answer' });
    });

    it('carries the reason and the status a failure is explained by', () => {
        expect(failed('unauthenticated', 401)).toEqual({
            outcome: 'failed',
            failure: { reason: 'unauthenticated', status: 401 },
        });
    });
});
