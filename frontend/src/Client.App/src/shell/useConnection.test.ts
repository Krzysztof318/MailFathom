// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientResponse } from '@mailfathom/client-backend';
import type { DeploymentTransport } from '../deployment/sendToDeployment';
import { mostReconnectionAttempts, reconnectionDelay, useConnection, type SignedInCaller } from './useConnection';

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

// Two people rather than two credentials, because that is what the hook keys on: a renewal replaces the header value
// under one identity, and only signing in as somebody else is a different one.
const firstPerson = { identity: 'karolina', authorization: 'Bearer mfs_first.c2Vzc2lvbg' };
const secondPerson = { identity: 'somebody', authorization: 'Bearer mfs_second.c2Vzc2lvbg' };

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
            useConnection(baseAddress, firstPerson, deploymentAnswering, nothingToDo, clock),
        );

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        expect(result.current.readAt).toEqual(readAt);
    });

    // A renewal replaces the header value roughly once every eleven hours while somebody is reading. Nothing about the
    // deployment changed, so nothing may be re-read and nothing may leave the screen: a hook keyed on the value would
    // empty the frame mid-morning and put the person back on 'Reaching your deployment…'.
    it('keeps what it read when the session it presents is renewed under the same person', async () => {
        const presented: (string | undefined)[] = [];
        const recording: DeploymentTransport = () => (request) => {
            presented.push(request.headers['Authorization']);

            return Promise.resolve(request.path.endsWith('/session') ? readsMail : oneAccount);
        };

        const { result, rerender } = renderHook(
            ({ signedIn }: { signedIn: SignedInCaller }) =>
                useConnection(baseAddress, signedIn, recording, nothingToDo, clock),
            { initialProps: { signedIn: firstPerson } },
        );

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        const readSoFar = presented.length;

        rerender({ signedIn: { ...firstPerson, authorization: 'Bearer mfs_renewed.c2Vzc2lvbg' } });

        expect(result.current.accounts?.outcome).toBe('read');
        expect(result.current.session?.outcome).toBe('read');
        expect(presented.length).toBe(readSoFar);

        // What the renewal is for: the next request carries the token the deployment now holds. The one it replaced
        // stopped working the moment the renewal answered, so a hook that kept presenting it would be refused.
        act(() => {
            result.current.reread();
        });

        await waitFor(() => {
            expect(presented.length).toBeGreaterThan(readSoFar);
        });

        expect(presented.at(-1)).toBe('Bearer mfs_renewed.c2Vzc2lvbg');
    });

    // The account signals one re-read every time a synchronization run finishes, which in push mode is every message
    // that arrives. A hook that discarded its answer for the duration would empty the frame on each of them: every
    // screen below unmounts, their reads are abandoned and reported as a deployment that is not answering, the list
    // returns to the top of the folder, and the signal channel is closed and opened again.
    it('keeps what it read on the screen while it reads again for the same person', async () => {
        let answer: ((response: ClientResponse) => void) | null = null;
        const holdingTheSecondRead: DeploymentTransport = () => (request) => {
            const response = request.path.endsWith('/session') ? readsMail : oneAccount;

            if (answer === null) {
                return Promise.resolve(response);
            }

            return new Promise<ClientResponse>((settle) => {
                answer = settle;
            }).then(() => response);
        };

        const { result } = renderHook(() =>
            useConnection(baseAddress, firstPerson, holdingTheSecondRead, nothingToDo, clock),
        );

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        // Held from here on, so the re-read below stays in flight for the whole of the assertion.
        answer = () => undefined;

        act(() => {
            result.current.reread();
        });

        expect(result.current.session?.outcome).toBe('read');
        expect(result.current.accounts?.outcome).toBe('read');
        expect(result.current.readAt).toEqual(readAt);
    });

    it('reads the session and the accounts again on a refresh', async () => {
        const paths: string[] = [];
        const recording: DeploymentTransport = () => (request) => {
            paths.push(request.path);

            return Promise.resolve(request.path.endsWith('/session') ? readsMail : oneAccount);
        };

        const { result } = renderHook(() => useConnection(baseAddress, firstPerson, recording, nothingToDo, clock));

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        const readSoFar = paths.length;

        act(() => {
            result.current.refresh();
        });

        await waitFor(() => {
            expect(paths.slice(readSoFar).some((path) => path.endsWith('/session'))).toBe(true);
            expect(paths.slice(readSoFar).some((path) => !path.endsWith('/session'))).toBe(true);
        });
    });

    // A refresh is the client's own and nobody is waiting on it, so one the deployment did not answer is the next one's
    // to repeat: the frame that stood stays, rather than being replaced by a deployment that stopped answering.
    it('keeps what it read on the screen when a refresh is not answered', async () => {
        let failing = false;
        const failingLater: DeploymentTransport = () => (request) =>
            failing
                ? Promise.resolve({ status: 503, body: '', headers: {} })
                : Promise.resolve(request.path.endsWith('/session') ? readsMail : oneAccount);

        const { result } = renderHook(() => useConnection(baseAddress, firstPerson, failingLater, nothingToDo, clock));

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        failing = true;

        act(() => {
            result.current.refresh();
        });
        await act(async () => {
            for (let turn = 0; turn < 10; turn += 1) {
                await Promise.resolve();
            }
        });

        expect(result.current.session?.outcome).toBe('read');
        expect(result.current.accounts?.outcome).toBe('read');
    });

    // The other half of keeping what was read: a grant that stopped reading mail asks for no accounts at all, so
    // nothing later in the attempt replaces the tree that stands — and one left up goes on offering mail the
    // deployment has begun refusing, for as long as that person stays signed in.
    it('drops what it read once the grant it read under stops reading mail', async () => {
        const readsNothing = answering({
            service: 'MailFathom',
            version: '0.8.7',
            permissions: [],
        });

        let narrowed = false;
        const narrowingTheGrant: DeploymentTransport = () => (request) => {
            if (request.path.endsWith('/session')) {
                return Promise.resolve(narrowed ? readsNothing : readsMail);
            }

            return Promise.resolve(oneAccount);
        };

        const { result } = renderHook(() =>
            useConnection(baseAddress, firstPerson, narrowingTheGrant, nothingToDo, clock),
        );

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        narrowed = true;

        act(() => {
            result.current.reread();
        });

        await waitFor(() => {
            expect(result.current.accounts).toBeNull();
        });

        expect(result.current.readAt).toBeNull();
        expect(result.current.session?.outcome).toBe('read');
    });

    // The comparison that decides what is drawn is the address and the person, and only they: the previous user's
    // accounts and the previous user's grants must not stand while the next person's read is out.
    it('draws nothing of the last person once somebody else signs in', async () => {
        const { result, rerender } = renderHook(
            ({ signedIn }: { signedIn: SignedInCaller }) =>
                useConnection(baseAddress, signedIn, deploymentAnswering, nothingToDo, clock),
            { initialProps: { signedIn: firstPerson } },
        );

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        rerender({ signedIn: secondPerson });

        expect(result.current.session).toBeNull();
        expect(result.current.accounts).toBeNull();
    });

    // A renewal destroys the token it replaced the moment the deployment answers, so a read already on the wire comes
    // back refused for a value this client itself replaced. Signing somebody out over that would discard the session
    // the renewal just minted, which is the ordinary path for a client opened inside the renewal margin.
    it('reads again rather than signing out when the refused token is one it has already replaced', async () => {
        const refusing: DeploymentTransport = () => (request) => {
            if (request.headers['Authorization'] !== 'Bearer mfs_renewed.c2Vzc2lvbg') {
                return Promise.resolve({ status: 401, body: '', headers: {} });
            }

            return Promise.resolve(request.path.endsWith('/session') ? readsMail : oneAccount);
        };

        let refused = 0;

        // Stable across renders like every other argument here, and for a sharper reason: a new callback each render is
        // a new dependency each render, which would restart the read and abandon the one this test is about.
        const countRefusal = (): void => {
            refused += 1;
        };

        const { result, rerender } = renderHook(
            ({ signedIn }: { signedIn: SignedInCaller }) =>
                useConnection(baseAddress, signedIn, refusing, countRefusal, clock),
            { initialProps: { signedIn: firstPerson } },
        );

        rerender({ signedIn: { ...firstPerson, authorization: 'Bearer mfs_renewed.c2Vzc2lvbg' } });

        await waitFor(() => {
            expect(result.current.accounts?.outcome).toBe('read');
        });

        expect(refused).toBe(0);
    });

    it('hands the next person a budget of their own rather than one the last one spent', async () => {
        vi.useFakeTimers();

        const { result, rerender } = renderHook(
            ({ signedIn }: { signedIn: SignedInCaller }) =>
                useConnection(baseAddress, signedIn, deploymentFailing, nothingToDo, clock),
            { initialProps: { signedIn: firstPerson } },
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

        rerender({ signedIn: secondPerson });

        expect(result.current.attempts).toBe(0);

        await act(async () => {
            await vi.advanceTimersByTimeAsync(0);
        });

        // The new credential's first read has failed once, which is one failure rather than a spent budget: the client
        // still owes them every automatic attempt instead of telling them the deployment has stopped answering.
        expect(result.current.attempts).toBe(0);
    });
});
