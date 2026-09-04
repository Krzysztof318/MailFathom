// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    ClientSession,
    ClientSignal,
    MailFathomTransport,
    SignalChannelOpening,
    SignalStreamSchedule,
} from '@mailfathom/client-backend';
import { useSignals } from './useSignals';

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

/** Lets the ticket read and the opening that follows it settle, neither being synchronous. */
async function settled(): Promise<void> {
    await act(async () => {
        for (let turn = 0; turn < 20; turn += 1) {
            await Promise.resolve();
        }
    });
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
        const first: ClientSignal[] = [];
        const second: ClientSignal[] = [];

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
        const told: ClientSignal[] = [];

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
        const told: ClientSignal[] = [];

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
