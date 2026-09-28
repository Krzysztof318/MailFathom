// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mostThrottledRetries, throttledRetryDelay } from './throttling';

const throttled = { status: 429, headers: {} };

describe('throttledRetryDelay', () => {
    it('waits a spread quarter of a second before the first retry of a refusal naming no moment', () => {
        expect(throttledRetryDelay(throttled, 0, 0)).toBe(188);
        expect(throttledRetryDelay(throttled, 0, 0.5)).toBe(250);
        expect(throttledRetryDelay(throttled, 0, 0.999)).toBe(312);
    });

    it('doubles the wait for every retry already made', () => {
        expect(throttledRetryDelay(throttled, 1, 0.5)).toBe(500);
        expect(throttledRetryDelay(throttled, 2, 0.5)).toBe(1_000);
    });

    it('stops once the retries are spent, so a refusal that persists is the answer', () => {
        expect(throttledRetryDelay(throttled, mostThrottledRetries, 0.5)).toBeNull();
    });

    it('never retries sooner than the Retry-After the deployment named', () => {
        const answer = { status: 429, headers: { 'retry-after': '2' } };

        expect(throttledRetryDelay(answer, 0, 0)).toBe(2_000);
        expect(throttledRetryDelay(answer, 0, 0.999)).toBe(2_500);
    });

    it('gives up at once on a Retry-After longer than a screen is held for', () => {
        expect(throttledRetryDelay({ status: 429, headers: { 'retry-after': '60' } }, 0, 0.5)).toBeNull();
    });

    it('reads a Retry-After in the date form as naming no moment', () => {
        const answer = { status: 429, headers: { 'retry-after': 'Wed, 21 Oct 2026 07:28:00 GMT' } };

        expect(throttledRetryDelay(answer, 0, 0.5)).toBe(250);
    });

    // A spent provider allowance is a refusal of the operation, which the drafting and arranging routes read as its own
    // sentence; asking again would only delay that sentence into the same answer.
    it('hands back a 429 carrying a problem document rather than retrying it', () => {
        const answer = { status: 429, headers: { 'content-type': 'application/problem+json; charset=utf-8' } };

        expect(throttledRetryDelay(answer, 0, 0.5)).toBeNull();
    });

    it.each([200, 401, 403, 500, 503])('hands back a %i at once', (status) => {
        expect(throttledRetryDelay({ status, headers: {} }, 0, 0.5)).toBeNull();
    });
});
