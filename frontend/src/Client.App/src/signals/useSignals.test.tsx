// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientSession,
    ClientSignal,
    MailFathomTransport,
    SignalChannelOpening,
    SignalStreamSchedule,
} from '@mailfathom/client-backend';
import type { SignalledChange, SignalledChanges } from './signalledChanges';
import { refreshInterval, useSignals } from './useSignals';

// What is proven here is the connection's lifetime and the fan-out, because those are what this hook owns: the package
// decides what a payload has to be and when to open again, and the channel itself is the composition root's. So the
// channel is a fake that hands back a socket a test can speak through, and the deployment answers exactly one route —
// the ticket every connection is opened against.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const somebodyElse: ClientSession = { ...session, authorization: 'Basic b3RoZXI=' };

const arrival: ClientSignal = { kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 3 };

const mintsTickets: MailFathomTransport = () =>
    Promise.resolve({
        status: 200,
        headers: {},
        body: JSON.stringify({ ticket: 'identity.cHJvb2Y', expiresAt: '2026-09-04T12:00:30+00:00' }),
    });

/** A schedule whose wait never elapses, so nothing in a test reopens a connection it did not ask to be reopened. */
const neverReopens: SignalStreamSchedule = {
    wait: () => new Promise<void>(() => undefined),
    draw: () => 0,
};

/** A channel a test speaks through: it records what was opened and hands back the way to push a payload down it. */
function channelHoldingOneConnection(): {
    channel: (opening: SignalChannelOpening) => Promise<{ close: () => Promise<void> }>;
    opened: SignalChannelOpening[];
    closedCount: () => number;
} {
    const opened: SignalChannelOpening[] = [];
    let closed = 0;

    return {
        opened,
        closedCount: () => closed,
        channel: (opening) => {
            opened.push(opening);

            return Promise.resolve({
                close: () => {
                    closed += 1;

                    return Promise.resolve();
                },
            });
        },
    };
}

/** A schedule whose wait is over at once, so a connection that dropped is opened again within one settling. */
const reopensAtOnce: SignalStreamSchedule = {
    wait: () => Promise.resolve(),
    draw: () => 0,
};

/** Lets the ticket read and the opening that follows it settle, neither being synchronous. */
async function settled(): Promise<void> {
    await act(async () => {
        for (let turn = 0; turn < 20; turn += 1) {
            await Promise.resolve();
        }
    });
}

/** Moves the scheduled clock on, letting whatever it set off settle. */
async function pass(milliseconds: number): Promise<void> {
    await act(async () => {
        await vi.advanceTimersByTimeAsync(milliseconds);
    });
}

/** Whether the window is on the screen, which is what the interval asks before it refreshes. */
function windowIs(state: DocumentVisibilityState): void {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state);
}

function windowComesBack(): void {
    windowIs('visible');
    act(() => {
        document.dispatchEvent(new Event('visibilitychange'));
    });
}

/** Listens to a hook's changes, answering how many of what it heard were refreshes. */
function listening(changes: SignalledChanges): () => number {
    const told: SignalledChange[] = [];

    act(() => {
        changes.listen((change) => told.push(change));
    });

    return () => told.filter((change) => change.kind === 'refresh').length;
}

describe('useSignals', () => {
    it('opens one connection once somebody is signed in', async () => {
        const deployment = channelHoldingOneConnection();

        renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));
        await settled();

        expect(deployment.opened).toHaveLength(1);
        expect(deployment.opened[0]?.url).toContain('/api/client/signals');
    });

    it('opens nothing where nobody is signed in', async () => {
        const deployment = channelHoldingOneConnection();

        renderHook(() => useSignals(null, mintsTickets, deployment.channel, neverReopens));
        await settled();

        expect(deployment.opened).toHaveLength(0);
    });

    it('tells every listener what the deployment said', async () => {
        const deployment = channelHoldingOneConnection();
        const first: SignalledChange[] = [];
        const second: SignalledChange[] = [];

        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));

        await settled();
        act(() => {
            view.result.current.listen((signal) => first.push(signal));
            view.result.current.listen((signal) => second.push(signal));
        });

        act(() => {
            deployment.opened[0]?.arrived(arrival);
        });

        expect(first).toStrictEqual([arrival]);
        expect(second).toStrictEqual([arrival]);
    });

    it('says nothing to a listener that has stopped listening', async () => {
        const deployment = channelHoldingOneConnection();
        const told: SignalledChange[] = [];

        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));

        await settled();

        let stop = (): void => undefined;

        act(() => {
            stop = view.result.current.listen((signal) => told.push(signal));
        });

        act(() => {
            stop();
            deployment.opened[0]?.arrived(arrival);
        });

        expect(told).toStrictEqual([]);
    });

    it('says nothing about a payload the deployment could not have sent', async () => {
        const deployment = channelHoldingOneConnection();
        const told: SignalledChange[] = [];

        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));

        await settled();
        act(() => {
            view.result.current.listen((signal) => told.push(signal));
        });

        act(() => {
            deployment.opened[0]?.arrived({ kind: 'mail.invented', account: 'work' });
        });

        expect(told).toStrictEqual([]);
    });

    it('closes the connection when the credential goes', async () => {
        const deployment = channelHoldingOneConnection();
        let signedInAs: ClientSession | null = session;

        const view = renderHook(() => useSignals(signedInAs, mintsTickets, deployment.channel, neverReopens));

        await settled();
        signedInAs = null;
        view.rerender();
        await settled();

        expect(deployment.closedCount()).toBe(1);
        expect(deployment.opened).toHaveLength(1);
    });

    it('opens a connection of its own for the next person signed in', async () => {
        const deployment = channelHoldingOneConnection();
        let signedInAs: ClientSession | null = session;

        const view = renderHook(() => useSignals(signedInAs, mintsTickets, deployment.channel, neverReopens));

        await settled();
        signedInAs = somebodyElse;
        view.rerender();
        await settled();

        expect(deployment.closedCount()).toBe(1);
        expect(deployment.opened).toHaveLength(2);
    });

    it('keeps one way to subscribe across a render, so a screen subscribing does not reopen the connection', async () => {
        const deployment = channelHoldingOneConnection();

        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));

        await settled();

        const before = view.result.current;

        view.rerender();

        expect(view.result.current).toBe(before);
        expect(deployment.opened).toHaveLength(1);
    });
});

describe('useSignals catching up', () => {
    afterEach(() => {
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    it('refreshes nothing when the first connection stands, there being nothing it could have missed', async () => {
        const deployment = channelHoldingOneConnection();
        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, reopensAtOnce));
        const refreshes = listening(view.result.current);

        await settled();

        expect(deployment.opened).toHaveLength(1);
        expect(refreshes()).toBe(0);
    });

    it('refreshes every screen once a connection stands again after dropping', async () => {
        const deployment = channelHoldingOneConnection();
        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, reopensAtOnce));
        const refreshes = listening(view.result.current);

        await settled();
        act(() => {
            deployment.opened[0]?.dropped();
        });
        await settled();

        expect(deployment.opened).toHaveLength(2);
        expect(refreshes()).toBe(1);
    });

    // The network going closes the stream and its coming back opens another for the same person, which is a gap in what
    // the screens were told exactly as a drop is.
    it('refreshes every screen when the network comes back for the same person', async () => {
        const deployment = channelHoldingOneConnection();
        let signedInAs: ClientSession | null = session;
        const view = renderHook(() => useSignals(signedInAs, mintsTickets, deployment.channel, neverReopens));
        const refreshes = listening(view.result.current);

        await settled();
        signedInAs = null;
        view.rerender();
        await settled();
        signedInAs = session;
        view.rerender();
        await settled();

        expect(deployment.opened).toHaveLength(2);
        expect(refreshes()).toBe(1);
    });

    it('refreshes nothing for a connection opened for somebody else, whose screens have only just been read', async () => {
        const deployment = channelHoldingOneConnection();
        let signedInAs: ClientSession | null = session;
        const view = renderHook(() => useSignals(signedInAs, mintsTickets, deployment.channel, neverReopens));
        const refreshes = listening(view.result.current);

        await settled();
        signedInAs = somebodyElse;
        view.rerender();
        await settled();

        expect(deployment.opened).toHaveLength(2);
        expect(refreshes()).toBe(0);
    });

    it('refreshes every five minutes while the window is visible', async () => {
        vi.useFakeTimers();
        windowIs('visible');
        const deployment = channelHoldingOneConnection();
        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));
        const refreshes = listening(view.result.current);

        await settled();
        await pass(refreshInterval - 1);
        expect(refreshes()).toBe(0);

        await pass(1);
        expect(refreshes()).toBe(1);

        await pass(refreshInterval);
        expect(refreshes()).toBe(2);
    });

    it('refreshes nothing while the window is hidden, and at once when it comes back after the interval ran out', async () => {
        vi.useFakeTimers();
        windowIs('hidden');
        const deployment = channelHoldingOneConnection();
        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));
        const refreshes = listening(view.result.current);

        await settled();
        await pass(refreshInterval * 3);
        expect(refreshes()).toBe(0);

        windowComesBack();
        expect(refreshes()).toBe(1);
    });

    it('refreshes nothing for a window that comes back before the interval ran out', async () => {
        vi.useFakeTimers();
        windowIs('hidden');
        const deployment = channelHoldingOneConnection();
        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));
        const refreshes = listening(view.result.current);

        await settled();
        await pass(refreshInterval / 5);
        windowComesBack();
        expect(refreshes()).toBe(0);

        await pass(refreshInterval);
        expect(refreshes()).toBe(1);
    });

    it('counts the interval from the last refresh rather than on a fixed clock', async () => {
        vi.useFakeTimers();
        windowIs('visible');
        const deployment = channelHoldingOneConnection();
        const view = renderHook(() => useSignals(session, mintsTickets, deployment.channel, neverReopens));
        const refreshes = listening(view.result.current);

        await settled();
        await pass(refreshInterval - 60_000);
        act(() => {
            view.result.current.refresh();
        });
        expect(refreshes()).toBe(1);

        await pass(60_000);
        expect(refreshes()).toBe(1);

        await pass(refreshInterval - 60_000);
        expect(refreshes()).toBe(2);
    });

    it('refreshes nothing, on the interval or when asked, where nobody is signed in', async () => {
        vi.useFakeTimers();
        windowIs('visible');
        const deployment = channelHoldingOneConnection();
        const view = renderHook(() => useSignals(null, mintsTickets, deployment.channel, neverReopens));
        const refreshes = listening(view.result.current);

        act(() => {
            view.result.current.refresh();
        });
        await pass(refreshInterval * 2);

        expect(refreshes()).toBe(0);
    });
});
