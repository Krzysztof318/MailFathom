// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mostReconnectionAttempts } from './reconnection';
import type { ClientSession } from './session';
import {
    hubAddressFor,
    mostNamedSignalEmails,
    openSignalStream,
    parseClientSignal,
    readSignalTicket,
    signalTicketParameter,
    type ClientSignal,
    type MailFathomSignalChannel,
    type SignalChannelOpening,
    type SignalStreamSchedule,
} from './signals';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

const minted = JSON.stringify({ ticket: 'abc.def', expiresAt: '2026-09-04T09:00:30+00:00' });

// Nothing here waits: the schedule is what this package asks its host for, so a test supplies one that resolves at once
// and records what it was asked to wait. A real timer would make every reconnection assertion a sleep.
function scheduleRecording(waits: number[]): SignalStreamSchedule {
    return {
        wait: (milliseconds) => {
            waits.push(milliseconds);

            return Promise.resolve();
        },
        draw: () => 0.5,
    };
}

/** Lets a test drive the channel the way a deployment would: opening it, sending a payload, and dropping it. */
function channelUnderTest(): {
    channel: MailFathomSignalChannel;
    openings: SignalChannelOpening[];
    closed: number[];
} {
    const openings: SignalChannelOpening[] = [];
    const closed: number[] = [];

    return {
        openings,
        closed,
        channel: (opening) => {
            openings.push(opening);

            const index = openings.length - 1;

            return Promise.resolve({
                close: () => {
                    closed.push(index);

                    return Promise.resolve();
                },
            });
        },
    };
}

// The stream runs on its own promise chain and this package declares no timer, so a test lets that chain run by
// yielding the microtask queue rather than by waiting: every boundary in it is an already-resolved promise, so the
// chain advances one turn at a time and a generous number of turns reaches whatever it is waiting on next.
async function settle(): Promise<void> {
    for (let turn = 0; turn < 500; turn += 1) {
        await Promise.resolve();
    }
}

describe('readSignalTicket', () => {
    it('mints over the ticket route with the session credential', async () => {
        const { transport, requests } = recording({ status: 200, body: minted });

        await readSignalTicket(session, transport);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/signals/ticket');
        expect(requests[0]?.headers['Authorization']).toBe(session.authorization);
    });

    it('reads the ticket and when presenting it stops working', async () => {
        const result = await readSignalTicket(session, answering({ status: 200, body: minted }));

        expect(result).toStrictEqual({
            outcome: 'read',
            value: { ticket: 'abc.def', expiresAt: '2026-09-04T09:00:30+00:00' },
        });
    });

    it('reports a refused credential as one to sign in again with', async () => {
        const result = await readSignalTicket(session, answering({ status: 401, body: '' }));

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });

    it('reports a deployment holding as many tickets as it will as one to try again', async () => {
        const result = await readSignalTicket(session, answering({ status: 503, body: '' }));

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 503 } });
    });

    it('refuses an answer that is not a ticket', async () => {
        const result = await readSignalTicket(session, answering({ status: 200, body: '{"ticket":42}' }));

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('hubAddressFor', () => {
    it('puts the ticket on the hub address, encoded', () => {
        expect(hubAddressFor(session, 'a b')).toBe(
            `https://mail.example.invalid/api/client/signals?${signalTicketParameter}=a%20b`,
        );
    });
});

describe('parseClientSignal', () => {
    it('reads an arrival as its account, its folder, and how much arrived', () => {
        expect(parseClientSignal({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 4 })).toStrictEqual({
            kind: 'mail.arrived',
            account: 'work',
            folder: 'INBOX',
            count: 4,
        });
    });

    it('reads a change as the rows to read again', () => {
        expect(
            parseClientSignal({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['m-1', 'm-2'] }),
        ).toStrictEqual({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['m-1', 'm-2'] });
    });

    it('reads a moved folder set as the account whose tree to read again', () => {
        expect(parseClientSignal({ kind: 'folders.changed', account: 'work' })).toStrictEqual({
            kind: 'folders.changed',
            account: 'work',
        });
    });

    it('reads a raised notification as its two lines and what stands unread', () => {
        expect(
            parseClientSignal({
                kind: 'notification.raised',
                notificationKind: 'Mail',
                headline: 'Mail arrived',
                secondLine: '4 new messages arrived.',
                count: 3,
            }),
        ).toStrictEqual({
            kind: 'notification.raised',
            notificationKind: 'Mail',
            headline: 'Mail arrived',
            secondLine: '4 new messages arrived.',
            unreadCount: 3,
        });
    });

    it('reads a finished run as the account to read again', () => {
        expect(parseClientSignal({ kind: 'account.state', account: 'work' })).toStrictEqual({
            kind: 'account.state',
            account: 'work',
        });
    });

    const refused: readonly (readonly [string, unknown])[] = [
        ['nothing at all', null],
        ['an array', []],
        ['a kind this client does not act on', { kind: 'mail.vanished', account: 'work' }],
        ['an arrival of nothing', { kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 0 }],
        ['an arrival with no folder', { kind: 'mail.arrived', account: 'work', count: 4 }],
        ['a change whose rows are not a list', { kind: 'mail.changed', account: 'work', folder: 'I', emails: 'm-1' }],
        ['a raised notification of an unknown kind', { kind: 'notification.raised', notificationKind: 'Weather' }],
        ['a moved folder set naming no account', { kind: 'folders.changed' }],
    ];

    it.each(refused)('refuses %s', (_, payload) => {
        expect(parseClientSignal(payload)).toBeNull();
    });

    it('refuses a change naming more rows than the deployment names', () => {
        const emails = Array.from({ length: mostNamedSignalEmails + 1 }, (_, index) => `m-${String(index)}`);

        expect(parseClientSignal({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails })).toBeNull();
    });

    it('refuses a value longer than the deployment would ever send', () => {
        const account = 'a'.repeat(257);

        expect(parseClientSignal({ kind: 'account.state', account })).toBeNull();
    });
});

describe('openSignalStream', () => {
    it('mints a ticket and opens the connection at the hub address', async () => {
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
            scheduleRecording([]),
        );

        await settle();

        expect(channel.openings).toHaveLength(1);
        expect(channel.openings[0]?.url).toBe(hubAddressFor(session, 'abc.def'));

        await stream.close();
    });

    it('tells the caller what arrived, once it is one of the five', async () => {
        const told: ClientSignal[] = [];
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            (signal) => told.push(signal),
            scheduleRecording([]),
        );

        await settle();
        channel.openings[0]?.arrived({ kind: 'folders.changed', account: 'work' });
        channel.openings[0]?.arrived({ kind: 'nonsense' });

        expect(told).toStrictEqual([{ kind: 'folders.changed', account: 'work' }]);

        await stream.close();
    });

    it('opens again after a connection dropped, waiting the schedule out first', async () => {
        const waits: number[] = [];
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
            scheduleRecording(waits),
        );

        await settle();
        channel.openings[0]?.dropped();
        await settle();

        expect(channel.openings).toHaveLength(2);
        expect(waits).toStrictEqual([1_000]);

        await stream.close();
    });

    it('gives up after the bounded number of attempts against a deployment serving no channel', async () => {
        const waits: number[] = [];
        const stream = openSignalStream(
            session,
            answering({ status: 404, body: '' }),
            () => Promise.reject(new Error('never asked')),
            () => undefined,
            scheduleRecording(waits),
        );

        await settle();

        expect(waits).toHaveLength(mostReconnectionAttempts);
        expect(waits.at(-1)).toBe(30_000);

        await stream.close();
    });

    it('closes what is open and opens nothing more', async () => {
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
            scheduleRecording([]),
        );

        await settle();
        await stream.close();
        await settle();

        expect(channel.closed).toStrictEqual([0]);
        expect(channel.openings).toHaveLength(1);
    });
});
