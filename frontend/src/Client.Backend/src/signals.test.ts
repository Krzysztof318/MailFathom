// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import { InMemoryLogRecordExporter, LoggerProvider, SimpleLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { mostReconnectionAttempts } from './reconnection';
import { defaultTelemetryLevel, recordDownTo } from './telemetry';
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

// The same, except that the wait after the last one it was told to allow never ends. A stream that never stops trying
// would otherwise try forever inside one test, so this is what lets a test count how far past the budget it went.
function scheduleWaiting(waits: number[], allowed: number): SignalStreamSchedule {
    return {
        wait: (milliseconds) => {
            waits.push(milliseconds);

            return waits.length < allowed ? Promise.resolve() : new Promise<void>(() => undefined);
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

    it("reads a flag change as where each named row's flags now stand", () => {
        expect(
            parseClientSignal({
                kind: 'mail.flags.changed',
                account: 'work',
                folder: 'INBOX',
                flags: [
                    { email: 'm-1', isSeen: true, isFlagged: false },
                    { email: 'm-2', isFlagged: true },
                ],
            }),
        ).toStrictEqual({
            kind: 'mail.flags.changed',
            account: 'work',
            folder: 'INBOX',
            flags: [
                { email: 'm-1', isSeen: true, isFlagged: false },
                { email: 'm-2', isSeen: null, isFlagged: true },
            ],
        });
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
        ['a flag change stating nothing', { kind: 'mail.flags.changed', account: 'work', folder: 'INBOX', flags: [] }],
        [
            'a flag change whose statement is about neither flag',
            { kind: 'mail.flags.changed', account: 'work', folder: 'INBOX', flags: [{ email: 'm-1' }] },
        ],
        [
            'a flag change whose flag is not a flag',
            {
                kind: 'mail.flags.changed',
                account: 'work',
                folder: 'INBOX',
                flags: [{ email: 'm-1', isSeen: 'yes' }],
            },
        ],
        [
            'a flag change naming no row',
            { kind: 'mail.flags.changed', account: 'work', folder: 'INBOX', flags: [{ isSeen: true }] },
        ],
    ];

    it.each(refused)('refuses %s', (_, payload) => {
        expect(parseClientSignal(payload)).toBeNull();
    });

    it('refuses a change naming more rows than the deployment names', () => {
        const emails = Array.from({ length: mostNamedSignalEmails + 1 }, (_, index) => `m-${String(index)}`);

        expect(parseClientSignal({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails })).toBeNull();
    });

    it('refuses a flag change stating more rows than the deployment names', () => {
        const flags = Array.from({ length: mostNamedSignalEmails + 1 }, (_, index) => ({
            email: `m-${String(index)}`,
            isSeen: true,
        }));

        expect(parseClientSignal({ kind: 'mail.flags.changed', account: 'work', folder: 'INBOX', flags })).toBeNull();
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
            () => undefined,
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

    it('says a connection stands each time one does, the first and every one reopened after a drop', async () => {
        let stood = 0;
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
            () => {
                stood += 1;
            },
            scheduleRecording([]),
        );

        await settle();
        expect(stood).toBe(1);

        channel.openings[0]?.dropped();
        await settle();

        expect(stood).toBe(2);

        await stream.close();
    });

    it('says nothing stands against a deployment serving no channel', async () => {
        let stood = 0;
        const stream = openSignalStream(
            session,
            answering({ status: 404, body: '' }),
            () => Promise.reject(new Error('never asked')),
            () => undefined,
            () => {
                stood += 1;
            },
            scheduleWaiting([], 3),
        );

        await settle();

        expect(stood).toBe(0);

        await stream.close();
    });

    it('goes on trying past the budget at the longest wait rather than stopping', async () => {
        const waits: number[] = [];
        const stream = openSignalStream(
            session,
            answering({ status: 404, body: '' }),
            () => Promise.reject(new Error('never asked')),
            () => undefined,
            () => undefined,
            scheduleWaiting(waits, mostReconnectionAttempts + 3),
        );

        await settle();

        expect(waits).toHaveLength(mostReconnectionAttempts + 3);
        expect(waits.slice(mostReconnectionAttempts - 1)).toStrictEqual([30_000, 30_000, 30_000, 30_000]);

        await stream.close();
    });

    it('closes what is open and opens nothing more', async () => {
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
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

describe('what the connection records for an operator', () => {
    let records: InMemoryLogRecordExporter;
    let loggers: LoggerProvider;

    beforeEach(() => {
        records = new InMemoryLogRecordExporter();
        loggers = new LoggerProvider({ processors: [new SimpleLogRecordProcessor({ exporter: records })] });

        logs.setGlobalLoggerProvider(loggers);

        // The connection's whole life is written below the level a collector keeps by default, so a test asserting it
        // has to ask for it — which is the same thing an operator does when somebody reports a client that feels stale.
        recordDownTo('trace');
    });

    afterEach(async () => {
        logs.disable();
        recordDownTo(defaultTelemetryLevel);
        await loggers.shutdown();
    });

    function occurrences(): readonly unknown[] {
        return records.getFinishedLogRecords().map((one) => one.attributes['mailfathom.client.event']);
    }

    it('says when a connection stood and when it ended', async () => {
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
            () => undefined,
            scheduleRecording([]),
        );

        await settle();
        channel.openings[0]?.dropped();
        await settle();

        expect(occurrences()).toContain('signals_opened');
        expect(occurrences()).toContain('signals_dropped');

        await stream.close();
    });

    // The moment worth an operator's attention is the one where nothing about the client changes: past the budget the
    // wait stops growing and every screen is being read on the interval alone, which nobody looking at one can tell.
    it('says once, and above the default level, that the hub has been unreachable past the budget', async () => {
        const stream = openSignalStream(
            session,
            answering({ status: 404, body: '' }),
            () => Promise.reject(new Error('never asked')),
            () => undefined,
            () => undefined,
            scheduleWaiting([], mostReconnectionAttempts + 3),
        );

        await settle();

        const written = records.getFinishedLogRecords();
        const unreachable = written.filter(
            (one) => one.attributes['mailfathom.client.event'] === 'signals_unreachable',
        );

        expect(unreachable).toHaveLength(1);
        expect(unreachable[0]?.severityNumber).toBe(SeverityNumber.WARN);
        expect(unreachable[0]?.attributes['mailfathom.client.attempt']).toBe(mostReconnectionAttempts);
        expect(
            written.filter((one) => one.attributes['mailfathom.client.event'] === 'signals_refused').length,
        ).toBeGreaterThan(0);

        await stream.close();
    });

    it('names the kind of statement that arrived and nothing else about it', async () => {
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
            () => undefined,
            scheduleRecording([]),
        );

        await settle();
        channel.openings[0]?.arrived({ kind: 'folders.changed', account: 'work' });
        await settle();

        const arrived = records
            .getFinishedLogRecords()
            .find((one) => one.attributes['mailfathom.client.event'] === 'signal_received');

        expect(arrived?.severityNumber).toBe(SeverityNumber.TRACE);
        expect(arrived?.attributes).toEqual({
            'mailfathom.client.event': 'signal_received',
            'mailfathom.client.signal': 'folders.changed',
        });

        await stream.close();
    });

    // A payload this client does not act on means a deployment speaking a vocabulary the client in front of it does
    // not have, which is a version skew rather than a network — and it is silent to everybody but this record.
    it('says that a payload it does not act on arrived', async () => {
        const channel = channelUnderTest();
        const stream = openSignalStream(
            session,
            answering({ status: 200, body: minted }),
            channel.channel,
            () => undefined,
            () => undefined,
            scheduleRecording([]),
        );

        await settle();
        channel.openings[0]?.arrived({ kind: 'mail.rearranged', account: 'work' });
        await settle();

        expect(occurrences()).toContain('signal_refused');
        expect(occurrences()).not.toContain('signal_received');

        await stream.close();
    });
});
