// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mostRetries, retryDelay } from './transientFailures';

const throttled = { status: 429, headers: {} };
const nothingAnswered = null;

describe('retryDelay', () => {
    it('waits a spread quarter of a second before the first retry of a refusal naming no moment', () => {
        expect(retryDelay('GET', throttled, 0, 0)).toBe(188);
        expect(retryDelay('GET', throttled, 0, 0.5)).toBe(250);
        expect(retryDelay('GET', throttled, 0, 0.999)).toBe(312);
    });

    it('doubles the wait for every retry already made', () => {
        expect(retryDelay('GET', throttled, 1, 0.5)).toBe(500);
        expect(retryDelay('GET', throttled, 2, 0.5)).toBe(1_000);
    });

    it('stops once the retries are spent, so a failure that persists is the answer', () => {
        expect(retryDelay('GET', throttled, mostRetries, 0.5)).toBeNull();
        expect(retryDelay('GET', nothingAnswered, mostRetries, 0.5)).toBeNull();
    });

    it('never retries sooner than the Retry-After the deployment named', () => {
        const answer = { status: 429, headers: { 'retry-after': '2' } };

        expect(retryDelay('GET', answer, 0, 0)).toBe(2_000);
        expect(retryDelay('GET', answer, 0, 0.999)).toBe(2_500);
    });

    it('gives up at once on a Retry-After longer than a screen is held for', () => {
        expect(retryDelay('GET', { status: 429, headers: { 'retry-after': '60' } }, 0, 0.5)).toBeNull();
        expect(retryDelay('GET', { status: 503, headers: { 'retry-after': '60' } }, 0, 0.5)).toBeNull();
    });

    it('reads a Retry-After in the date form as naming no moment', () => {
        const answer = { status: 429, headers: { 'retry-after': 'Wed, 21 Oct 2026 07:28:00 GMT' } };

        expect(retryDelay('GET', answer, 0, 0.5)).toBe(250);
    });

    // The limiter refuses before any route runs, so the refusal proves nothing was done whatever the method.
    it.each(['POST', 'PUT', 'DELETE'])('retries a %s the limiter refused', (method) => {
        expect(retryDelay(method, throttled, 0, 0.5)).toBe(250);
    });

    // A spent provider allowance is a refusal of the operation, which the drafting and arranging routes read as its own
    // sentence; asking again would only delay that sentence into the same answer.
    it('hands back a 429 carrying a problem document rather than retrying it', () => {
        const answer = { status: 429, headers: { 'content-type': 'application/problem+json; charset=utf-8' } };

        expect(retryDelay('GET', answer, 0, 0.5)).toBeNull();
    });

    it.each([502, 503, 504])('retries a read answered %i', (status) => {
        expect(retryDelay('GET', { status, headers: {} }, 0, 0.5)).toBe(250);
    });

    it('waits out the Retry-After a deployment not answering for now named', () => {
        expect(retryDelay('GET', { status: 503, headers: { 'retry-after': '4' } }, 0, 0)).toBe(4_000);
    });

    it('retries a read nothing answered at all', () => {
        expect(retryDelay('GET', nothingAnswered, 0, 0.5)).toBe(250);
    });

    // A gateway that timed out, and a connection cut short, may have handed the write on before failing.
    it.each(['POST', 'PUT', 'DELETE'])('never retries a %s after a failure that proves nothing', (method) => {
        expect(retryDelay(method, nothingAnswered, 0, 0.5)).toBeNull();
        expect(retryDelay(method, { status: 502, headers: {} }, 0, 0.5)).toBeNull();
        expect(retryDelay(method, { status: 503, headers: { 'retry-after': '1' } }, 0, 0.5)).toBeNull();
        expect(retryDelay(method, { status: 504, headers: {} }, 0, 0.5)).toBeNull();
    });

    it.each([200, 204, 400, 401, 403, 404, 409, 500])('hands back a %i at once', (status) => {
        expect(retryDelay('GET', { status, headers: {} }, 0, 0.5)).toBeNull();
    });
});
