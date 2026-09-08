// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { KeptSession } from './keptSession';
import { renewalCheckInterval, renewalMargin, useSessionRenewal } from './useSessionRenewal';

// The clock is fake throughout, because what this hook is is a reading of the clock: nothing here waits for a margin
// measured in hours, and nothing asserts on a timer having been created.

const now = new Date('2026-09-08T09:00:00+00:00');

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Bearer mfs_heldsession.aGVsZC1zZXNzaW9u',
};

const renewedToken = 'mfs_renewedsession.cmVuZXdlZC1zZXNzaW9u';

/** A session that ends far enough out that renewing it is not due, which is what every start here is. */
function endingIn(milliseconds: number): KeptSession {
    return {
        authorization: session.authorization,
        expiresAt: new Date(now.getTime() + milliseconds).toISOString(),
        person: 'karolina',
    };
}

function answering(response: Partial<ClientResponse>): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: '', headers: {}, ...response });
        },
    };
}

function mintedBody(): string {
    return JSON.stringify({ token: renewedToken, expiresAt: '2026-09-08T21:00:00+00:00' });
}

/**
 * Drives the hook the way the frame does, and reports what it did.
 *
 * The renewal is asynchronous inside a timer, so every advance of the clock is awaited rather than only advanced: a
 * test that ticked without letting the answer arrive would assert on the request and never on what was done with it.
 */
function driven(
    kept: KeptSession | null,
    transport: MailFathomTransport,
    online = true,
): { renewed: KeptSession[]; refused: number; settle: () => Promise<void>; tick: () => Promise<void> } {
    const renewed: KeptSession[] = [];
    let refused = 0;

    renderHook(() => {
        useSessionRenewal(
            kept === null ? null : session,
            kept,
            transport,
            online,
            (session) => {
                renewed.push(session);
            },
            () => {
                refused += 1;
            },
        );
    });

    return {
        renewed,
        get refused() {
            return refused;
        },
        // What a renewal started on arrival needs to arrive, and nothing more. The hook holds no state of its own —
        // the frame is what replaces the session a renewal answered — so a test that advanced a whole interval would
        // find the same session still due and be asserting on two renewals rather than on one.
        settle: async () => {
            await act(async () => {
                await vi.advanceTimersByTimeAsync(0);
            });
        },
        tick: async () => {
            await act(async () => {
                await vi.advanceTimersByTimeAsync(renewalCheckInterval);
            });
        },
    };
}

describe('useSessionRenewal', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(now);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('leaves a session alone while it has longer left than the margin renewing is due within', async () => {
        const { transport, asked } = answering({ body: mintedBody() });
        const running = driven(endingIn(renewalMargin * 4), transport);

        await running.tick();

        expect(asked).toEqual([]);
    });

    // Due on the first reading rather than on the first tick: a client opened with a session already inside the margin
    // renews on arrival, which is what a machine that was asleep across it comes back to.
    it('renews a session already inside the margin without waiting for a tick', async () => {
        const { transport, asked } = answering({ body: mintedBody() });
        const running = driven(endingIn(renewalMargin / 2), transport);

        await running.settle();

        expect(asked.map((request) => request.path)).toEqual(['https://mail.example.invalid/api/client/session/token']);
    });

    it('reports the session that came back under the person who was already signed in', async () => {
        const { transport } = answering({ body: mintedBody() });
        const running = driven(endingIn(renewalMargin / 2), transport);

        await running.settle();

        expect(running.renewed).toEqual([
            {
                authorization: `Bearer ${renewedToken}`,
                expiresAt: '2026-09-08T21:00:00+00:00',
                person: 'karolina',
            },
        ]);
    });

    // The margin is longer than the interval, so a renewal that stayed due would go out on every tick. One at a time
    // is what keeps a deployment that is slow to answer from being asked again while it is still answering the first.
    it('has one renewal in flight at a time rather than one per tick', async () => {
        const asked: ClientRequest[] = [];
        const running = driven(endingIn(renewalMargin / 2), (request) => {
            asked.push(request);

            return new Promise<ClientResponse>(() => undefined);
        });

        await running.tick();
        await running.tick();

        expect(asked.length).toBe(1);
    });

    // The renewal the deployment has already applied: it destroys the presented token the moment it answers, so an
    // answer dropped because a dependency changed mid-flight would leave the client holding a dead credential and
    // somebody signed out at the expiry for a network blip that lasted a second.
    it('applies a renewal that answered after this machine lost and regained its network', async () => {
        const renewed: KeptSession[] = [];
        const kept = endingIn(renewalMargin / 2);
        let answer: (response: ClientResponse) => void = () => undefined;

        const { rerender } = renderHook(
            ({ online }: { online: boolean }) => {
                useSessionRenewal(
                    session,
                    kept,
                    () =>
                        new Promise<ClientResponse>((resolve) => {
                            answer = resolve;
                        }),
                    online,
                    (renewal) => {
                        renewed.push(renewal);
                    },
                    () => undefined,
                );
            },
            { initialProps: { online: true } },
        );

        // Act
        rerender({ online: false });
        rerender({ online: true });

        await act(async () => {
            answer({ status: 200, body: mintedBody(), headers: {} });
            await vi.advanceTimersByTimeAsync(0);
        });

        // Assert
        expect(renewed.map((session) => session.authorization)).toEqual([`Bearer ${renewedToken}`]);
    });

    it('says nothing about a renewal the deployment could not be asked for, and asks again on the next tick', async () => {
        const asked: ClientRequest[] = [];
        const running = driven(endingIn(renewalMargin / 2), (request) => {
            asked.push(request);

            return Promise.reject(new TypeError('Failed to fetch'));
        });

        await running.tick();

        expect(running.renewed).toEqual([]);
        expect(running.refused).toBe(0);
        expect(asked.length).toBeGreaterThan(1);
    });

    // The one answer that is the session being over rather than the renewal not getting through, which is the same
    // path a revoked session takes: the deployment refuses it on the next request instead of at its expiry.
    it('reports a session the deployment refuses as one to sign in for again', async () => {
        const { transport } = answering({ status: 401 });
        const running = driven(endingIn(renewalMargin / 2), transport);

        await running.settle();

        expect(running.refused).toBe(1);
        expect(running.renewed).toEqual([]);
    });

    it('renews nothing while nobody is signed in', async () => {
        const { transport, asked } = answering({ body: mintedBody() });
        const running = driven(null, transport);

        await running.tick();

        expect(asked).toEqual([]);
    });

    it('renews nothing this machine has no network to deliver', async () => {
        const { transport, asked } = answering({ body: mintedBody() });
        const running = driven(endingIn(renewalMargin / 2), transport, false);

        await running.tick();

        expect(asked).toEqual([]);
    });
});
