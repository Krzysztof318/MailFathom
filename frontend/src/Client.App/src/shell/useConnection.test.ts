// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientResponse } from '@mailfathom/client-backend';
import type { DeploymentTransport } from '../deployment/sendToDeployment';
import { mostReconnectionAttempts, reconnectionDelay, useConnection } from './useConnection';

// The waiting the hook does between attempts, as a function of its arguments rather than of a clock or of a draw it
// made itself — which is what lets it be stated here without a fake timer and without stubbing randomness.

describe('reconnectionDelay', () => {
    it('waits longer after each attempt that did not answer', () => {
        const waits = [0, 1, 2, 3].map((made) => reconnectionDelay(made, 0.5));

        expect(waits).toEqual([...waits].sort((first, second) => first - second));
        expect(new Set(waits).size).toBe(waits.length);
    });

    it('stops lengthening the wait, so a deployment that is down is not left an hour behind', () => {
        expect(reconnectionDelay(20, 0.5)).toBe(reconnectionDelay(30, 0.5));
    });

    it('spreads the wait around the nominal one, so clients that lost one deployment do not return in step', () => {
        const nominal = reconnectionDelay(0, 0.5);

        expect(reconnectionDelay(0, 0)).toBeLessThan(nominal);
        expect(reconnectionDelay(0, 0.999)).toBeGreaterThan(nominal);
    });

    it.each([0, 0.25, 0.5, 0.75, 0.999])('waits a positive time whatever is drawn, here %s', (drawn) => {
        expect(reconnectionDelay(0, drawn)).toBeGreaterThan(0);
    });
});

// What the hook itself does with two things no screen can reach through it: the instant it stamps an answer with, and
// the budget it spends reaching for a deployment that is not answering. Everything the frame shows for either of them
// is asserted where a person would read it, in `App.test.tsx` and `ConnectionSummary.test.tsx`.

const baseAddress = 'https://mail.example.invalid';
const firstCredential = 'Basic b3duZXI6b3Blbg==';
const secondCredential = 'Basic c29tZWJvZHk6ZWxzZQ==';

// The instant this suite decided, which is what an answer is stamped with when the hook is handed it — never a system
// clock, so nothing here depends on the day it ran.
const readAt = new Date('2026-08-31T12:41:00Z');
const clock = (): Date => readAt;

// Stable across renders, all four of them: a new function each render is a new dependency each render, and the read
// effect would restart forever rather than answering once.
const nothingToDo = (): void => undefined;

function answering(body: Readonly<Record<string, unknown>>, status = 200): ClientResponse {
    return { status, body: JSON.stringify(body), headers: {} };
}

const readsMail = answering({
    service: 'MailFathom',
    version: '0.8.7',
    permissions: ['mailfathom.mail.read'],
});

const oneAccount = answering({
    synchronizationEnabled: true,
    accounts: [
        {
            id: 'work',
            displayName: 'Work',
            synchronizationState: 'Synchronized',
            lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
            behind: false,
        },
    ],
});

const deploymentAnswering: DeploymentTransport = () => (request) =>
    Promise.resolve(request.path.endsWith('/session') ? readsMail : oneAccount);

const deploymentFailing: DeploymentTransport = () => () => Promise.resolve({ status: 503, body: '', headers: {} });

describe('useConnection', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('stamps what it read with the instant its caller decided rather than with a clock of its own', async () => {
        const { result } = renderHook(() =>
            useConnection(baseAddress, firstCredential, deploymentAnswering, nothingToDo, clock),
        );

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        expect(result.current.readAt).toEqual(readAt);
    });

    it('hands the next person a budget of their own rather than one the last one spent', async () => {
        vi.useFakeTimers();

        const { result, rerender } = renderHook(
            ({ authorization }: { authorization: string }) =>
                useConnection(baseAddress, authorization, deploymentFailing, nothingToDo, clock),
            { initialProps: { authorization: firstCredential } },
        );

        await act(async () => {
            await vi.advanceTimersByTimeAsync(0);
        });

        // Each attempt is only scheduled once the one before it has answered, so the waiting is advanced once per
        // attempt rather than far enough to cover all of them at once.
        for (let spent = 0; spent < mostReconnectionAttempts; spent += 1) {
            await act(async () => {
                await vi.advanceTimersByTimeAsync(60_000);
            });
        }

        expect(result.current.attempts).toBe(mostReconnectionAttempts);

        rerender({ authorization: secondCredential });

        expect(result.current.attempts).toBe(0);

        await act(async () => {
            await vi.advanceTimersByTimeAsync(0);
        });

        // The new credential's first read has failed once, which is one failure rather than a spent budget: the client
        // still owes them every automatic attempt instead of telling them the deployment has stopped answering.
        expect(result.current.attempts).toBe(0);
    });
});
