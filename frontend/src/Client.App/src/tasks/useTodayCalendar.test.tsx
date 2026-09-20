// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { dayAround } from './dayInstants';
import { useTodayCalendar } from './useTodayCalendar';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// The probe's own control, named here rather than written into its markup: the localization rule holds over every
// `.tsx` under the packages, tests included.
const asksAgain = 'Read the day again';

// A fixed instant, so what window the panel asks for is a value this file can state rather than whatever the machine's
// clock says while the test runs.
const readingAt = Date.parse('2026-09-21T09:15:00+02:00');

function event(id: string, title: string, start: string): unknown {
    return {
        id,
        title,
        start,
        end: start,
        origin: 'Asserted',
        sourceMessage: null,
        recordedAt: start,
        amendedAt: start,
    };
}

function answering(answers: readonly (string | number)[]): {
    readonly transport: MailFathomTransport;
    readonly asked: () => readonly string[];
} {
    const asked: string[] = [];
    let served = 0;

    return {
        asked: () => asked,
        transport: ({ path }) => {
            asked.push(path);

            const answer = answers[Math.min(served, answers.length - 1)] ?? JSON.stringify({ events: [] });

            served += 1;

            return Promise.resolve(
                typeof answer === 'number'
                    ? { status: answer, body: '', headers: {} }
                    : { status: 200, body: answer, headers: {} },
            );
        },
    };
}

function Day({
    transport,
    asking = session,
}: {
    readonly transport: MailFathomTransport;
    readonly asking?: ClientSession | null;
}) {
    const held = useTodayCalendar(asking, transport, readingAt);

    return (
        <div>
            <p>{held.reading ? 'reading' : 'read'}</p>
            <p>{held.failure ?? 'no failure'}</p>
            <ol>
                {held.events.map((held) => (
                    <li key={held.id}>{held.title}</li>
                ))}
            </ol>
            <button onClick={held.readAgain}>{asksAgain}</button>
        </div>
    );
}

describe('useTodayCalendar', () => {
    it('asks for the reader’s own day, for what is on the calendar rather than what was proposed', async () => {
        const deployment = answering([JSON.stringify({ events: [] })]);
        const day = dayAround(readingAt);

        render(<Day transport={deployment.transport} />);

        await waitFor(() => {
            expect(screen.getByText('read')).toBeDefined();
        });

        expect(deployment.asked()[0]).toBe(
            `${session.baseAddress}/api/client/calendar?from=${encodeURIComponent(day.from)}&until=${encodeURIComponent(day.until)}&origin=Asserted&count=100`,
        );
    });

    it('draws what the day holds', async () => {
        render(
            <Day
                transport={
                    answering([
                        JSON.stringify({
                            events: [event('a', 'Standup', '2026-09-21T07:00:00+00:00')],
                        }),
                    ]).transport
                }
            />,
        );

        await waitFor(() => {
            expect(screen.getByText('Standup')).toBeDefined();
        });
    });

    it('says why the day did not answer, and reads it again when asked to', async () => {
        const deployment = answering([503, JSON.stringify({ events: [event('a', 'Standup', '2026-09-21T07:00:00+00:00')] })]);

        render(<Day transport={deployment.transport} />);

        await waitFor(() => {
            expect(screen.getByText('unavailable')).toBeDefined();
        });

        fireEvent.click(screen.getByRole('button', { name: asksAgain }));

        await waitFor(() => {
            expect(screen.getByText('Standup')).toBeDefined();
        });

        expect(screen.getByText('no failure')).toBeDefined();
    });

    it('asks for nothing where there is no credential to ask with', () => {
        const deployment = answering([JSON.stringify({ events: [] })]);

        render(<Day transport={deployment.transport} asking={null} />);

        expect(deployment.asked()).toStrictEqual([]);
        expect(screen.getByText('read')).toBeDefined();
    });
});
