// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { CalendarSpace } from './CalendarSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// Every day this screen computes is a day in the reader's own zone, so the zone is pinned rather than inherited: a
// run in UTC would draw a different week from a run in Warsaw and the assertions below would follow the machine.
const zoneBefore = process.env['TZ'];

// jsdom evaluates no media query, so the composition a test is about is stated as a width and read back out of the
// query itself, exactly as `people/PeopleSpace.test.tsx` does it.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

const phone = 390;
const desktop = 1440;

function atWidth(pixels: number): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => {
            const named = /([\d.]+)rem/u.exec(query)?.[1];

            return {
                matches: named !== undefined && pixels >= Number(named) * 16,
                media: query,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            };
        },
    });
}

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
    atWidth(desktop);
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;

    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

// A Thursday in the middle of a week that is wholly inside one month, so the week and the month cover different spans
// and a test can tell which one is drawn.
const standing = new Date(2026, 8, 24, 10);

function eventCalled(
    id: string,
    title: string,
    start: string,
    {
        end = null,
        origin = 'Asserted',
        sourceMessage = null,
        reminders = [],
    }: { end?: string | null; origin?: string; sourceMessage?: string | null; reminders?: readonly number[] } = {},
): unknown {
    return {
        id,
        title,
        start,
        end,
        isAllDay: false,
        reminders,
        remindsAt: [],
        origin,
        sourceMessage,
        recordedAt: '2026-09-01T09:00:00+02:00',
        amendedAt: '2026-09-01T09:00:00+02:00',
    };
}

const review = eventCalled('review', 'Review with Anna', '2026-09-24T09:00:00+02:00', {
    end: '2026-09-24T10:00:00+02:00',
});

const standup = eventCalled('standup', 'Stand-up', '2026-09-25T09:30:00+02:00');

const proposed = eventCalled('proposed', 'Lunch with Celina', '2026-09-24T13:00:00+02:00', {
    origin: 'Proposed',
    sourceMessage: 'message-4',
});

const answered = { status: 200, headers: {} } as const;

// One deployment behind the screen: a window of events, the capability behind the description field, and the four
// writes. A test states what a write answers and reads back what the screen did about it.
function deployment({
    events = [review, standup, proposed],
    readsDescriptions = true,
    refuses = [],
    refusesAcceptance = false,
}: {
    events?: readonly unknown[];

    /** Whether this deployment turns a typed description into an event at all. */
    readsDescriptions?: boolean;

    /** Whose deletion this deployment will not perform, which is how a batch is made to half succeed. */
    refuses?: readonly string[];

    /** Whether taking a proposal onto the calendar fails, which is what the card coming back is about. */
    refusesAcceptance?: boolean;
} = {}): { readonly transport: MailFathomTransport; readonly sent: () => readonly ClientRequest[] } {
    const sent: ClientRequest[] = [];

    return {
        sent: () => sent,
        transport: (request) => {
            sent.push(request);

            const { method, path } = request;
            let answer: ClientResponse;

            if (path.includes('/calendar/drafts')) {
                answer = { ...answered, body: JSON.stringify({ readsDescriptions }) };
            } else if (method === 'DELETE') {
                answer = refuses.some((event) => path.endsWith(`/calendar/${event}`))
                    ? { status: 503, headers: {}, body: '' }
                    : { status: 204, headers: {}, body: '' };
            } else if (method === 'POST' && path.endsWith('/acceptance') && refusesAcceptance) {
                answer = { status: 503, headers: {}, body: '' };
            } else if (method === 'POST' || method === 'PUT') {
                answer = { ...answered, body: JSON.stringify(review) };
            } else {
                answer = { ...answered, body: JSON.stringify({ events }) };
            }

            return Promise.resolve(answer);
        },
    };
}

function drawSpace(transport: MailFathomTransport, onOpenMessage = vi.fn()): void {
    render(
        <LocalizationProvider>
            <CalendarSpace session={session} transport={transport} now={() => standing} onOpenMessage={onOpenMessage} />
        </LocalizationProvider>,
    );
}

function windowsAsked(sent: readonly ClientRequest[]): readonly ClientRequest[] {
    return sent.filter((request) => request.method === 'GET' && !request.path.includes('/drafts'));
}

describe('CalendarSpace', () => {
    it('opens on the week the reader is standing in and reads that span once', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();
        expect(screen.getByRole('heading', { name: 'September 2026' })).toBeDefined();

        const asked = windowsAsked(sent());
        expect(asked).toHaveLength(1);

        // Monday to Monday, which is the week the design project draws and the window the deployment is asked for.
        expect(asked[0]?.path).toContain(`from=${encodeURIComponent(new Date(2026, 8, 21).toISOString())}`);
        expect(asked[0]?.path).toContain(`until=${encodeURIComponent(new Date(2026, 8, 28).toISOString())}`);
    });

    it('draws what is on the calendar in the views and what mail proposed beside them, never one in the other', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();
        expect(screen.queryByRole('option', { name: /Lunch with Celina/u })).toBeNull();

        const proposals = screen.getByRole('region', { name: 'Detected in mail' });
        expect(proposals.textContent).toContain('Lunch with Celina');
    });

    it('reads the new span when the reader moves, rather than filtering what it already holds', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Later' }));

        await waitFor(() => {
            expect(windowsAsked(sent())).toHaveLength(2);
        });

        expect(windowsAsked(sent())[1]?.path).toContain(
            `from=${encodeURIComponent(new Date(2026, 8, 28).toISOString())}`,
        );
    });

    it('reads the month when the month view is chosen, which covers whole weeks either side of it', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();

        fireEvent.click(screen.getByRole('radio', { name: 'Month' }));

        await waitFor(() => {
            expect(windowsAsked(sent())).toHaveLength(2);
        });

        // The thirty-first of August is the Monday of the week the first of September falls in.
        expect(windowsAsked(sent())[1]?.path).toContain(
            `from=${encodeURIComponent(new Date(2026, 7, 31).toISOString())}`,
        );
    });

    it('draws the week as an agenda below the width seven columns need, and says that is what it did', async () => {
        atWidth(phone);
        const { transport } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();
        expect(
            screen.getByText('This width has no room for the columns, so the same days are listed instead.'),
        ).toBeDefined();
    });

    it('takes a proposal onto the calendar and takes the card away before the read comes back', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByText('Lunch with Celina')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Add' }));

        expect(screen.queryByText('Lunch with Celina')).toBeNull();

        await waitFor(() => {
            expect(sent().some((request) => request.path.endsWith('/calendar/proposed/acceptance'))).toBe(true);
        });
    });

    it('puts a proposal back where the write did not happen, rather than leaving it answered', async () => {
        const { transport } = deployment({ refusesAcceptance: true });
        drawSpace(transport);

        expect(await screen.findByText('Lunch with Celina')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Add' }));

        expect(await screen.findByText('The deployment did not answer, so nothing was changed.')).toBeDefined();
        expect(screen.getByText('Lunch with Celina')).toBeDefined();
    });

    it('lets a proposal go by deleting it, and takes the card away as it goes', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByText('Lunch with Celina')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));

        expect(screen.queryByText('Lunch with Celina')).toBeNull();

        await waitFor(() => {
            expect(
                sent().some((request) => request.method === 'DELETE' && request.path.endsWith('/calendar/proposed')),
            ).toBe(true);
        });
    });

    it('draws the span the reader moved to, even where the span they left answers after it', async () => {
        // The two reads are held rather than answered, so the test decides which one arrives first: the ordering of
        // two requests in flight is not guaranteed, and last week's events under this week's heading would read as a
        // rendering defect rather than as the race it is.
        const held: ((answer: ClientResponse) => void)[] = [];

        const transport: MailFathomTransport = (request) =>
            request.path.includes('/calendar/drafts')
                ? Promise.resolve({ ...answered, body: JSON.stringify({ readsDescriptions: false }) })
                : new Promise((answer) => {
                      held.push(answer);
                  });

        drawSpace(transport);

        await waitFor(() => {
            expect(held).toHaveLength(1);
        });

        fireEvent.click(screen.getByRole('button', { name: 'Later' }));

        await waitFor(() => {
            expect(held).toHaveLength(2);
        });

        const arrived = eventCalled('handover', 'Warehouse handover', '2026-09-30T09:00:00+02:00');
        const late = eventCalled('carrier', 'Carrier review', '2026-09-30T14:00:00+02:00');

        held[1]?.({ ...answered, body: JSON.stringify({ events: [arrived] }) });

        expect(await screen.findByRole('option', { name: /Warehouse handover/u })).toBeDefined();

        // The week that was left answers last, and with an event inside the span now on the screen — so an answer
        // applied whatever it was asked for would be visible here rather than merely out of view. It is resolved
        // inside `act` because what is being asserted is that nothing follows it: an absence cannot be waited for, so
        // the render it would have caused is driven to completion first and read afterwards.
        await act(async () => {
            held[0]?.({ ...answered, body: JSON.stringify({ events: [late] }) });

            // The answer is an already-resolved promise, so one turn of the microtask queue is the whole of what
            // *after it arrived* means — and the render it would have caused has happened by the time `act` returns.
            await Promise.resolve();
        });

        expect(screen.getByRole('option', { name: /Warehouse handover/u })).toBeDefined();
        expect(screen.queryByRole('option', { name: /Carrier review/u })).toBeNull();
    });

    it('picks an entry out on a modifier-held press and says how many are held', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        const entry = await screen.findByRole('option', { name: /Review with Anna/u });

        fireEvent.click(entry, { ctrlKey: true });

        expect(entry.getAttribute('aria-selected')).toBe('true');
        expect(screen.getByRole('toolbar', { name: 'What to do with the selected events' })).toBeDefined();
        expect(screen.getByText('1 selected')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'New event' })).toBeNull();
    });

    it('opens the entry a plain press lands on rather than picking it out', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }));

        expect(await screen.findByRole('heading', { name: 'Review with Anna' })).toBeDefined();
        expect(screen.queryByText('1 selected')).toBeNull();
    });

    it('offers the same two gestures from the keyboard, a selection being unworkable without them', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        const entry = await screen.findByRole('option', { name: /Review with Anna/u });

        fireEvent.keyDown(entry, { key: 'Enter', ctrlKey: true });

        expect(entry.getAttribute('aria-selected')).toBe('true');
    });

    it('asks before deleting several, naming how many, and then deletes each of them', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('option', { name: /Stand-up/u }), { ctrlKey: true });

        expect(screen.getByText('2 selected')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(screen.getByText('Delete 2 events?')).toBeDefined();
        expect(screen.getByText('They leave the calendar and cannot be brought back.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Delete from the calendar' }));

        await waitFor(() => {
            expect(sent().filter((request) => request.method === 'DELETE')).toHaveLength(2);
        });
    });

    it('says so where one of a batch was refused rather than reporting the whole of it as done', async () => {
        const { transport } = deployment({ refuses: ['standup'] });
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('option', { name: /Stand-up/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
        fireEvent.click(screen.getByRole('button', { name: 'Delete from the calendar' }));

        expect(await screen.findByText('Some of them were not deleted.')).toBeDefined();
    });

    it('offers the event"s own menu on a right press, with the four acts this client can perform', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.contextMenu(await screen.findByRole('option', { name: /Review with Anna/u }));

        expect(await screen.findByRole('menuitem', { name: 'Select events' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Open the event' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Reminders' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Delete the event' })).toBeDefined();
    });

    it('opens the event with what announces it in front, where the menu asked for the reminders', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.contextMenu(await screen.findByRole('option', { name: /Review with Anna/u }));
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Reminders' }));

        expect(await screen.findByRole('dialog', { name: 'Reminders' })).toBeDefined();
    });

    it('opens a new event on the day the reader walked to, not on the one the screen was drawn on', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Later' }));
        fireEvent.click(await screen.findByRole('button', { name: 'New event' }));

        // Scoped to the dialog, the view control beside it carrying the same word: what is read here is the field the
        // event is written into, and the week walked to opens on its own Monday.
        const writing = within(screen.getByRole('dialog', { name: 'New event' }));

        expect(writing.getByLabelText<HTMLInputElement>('Day').value).toBe('2026-09-28');
    });

    it('draws the description field only where the deployment reads one', async () => {
        const { transport } = deployment({ readsDescriptions: false });
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'New event' }));

        expect(screen.getByLabelText('Title')).toBeDefined();
        expect(screen.queryByLabelText('Describe the event and MailFathom fills the fields')).toBeNull();
    });

    it('says why the span did not answer and offers the way out', async () => {
        const failing: MailFathomTransport = (request) =>
            Promise.resolve(
                request.path.includes('/drafts')
                    ? { ...answered, body: JSON.stringify({ readsDescriptions: false }) }
                    : { status: 503, headers: {}, body: '' },
            );

        drawSpace(failing);

        expect(
            await screen.findByText('The deployment did not answer with the calendar. It may be offline or starting.'),
        ).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('says the calendar is empty where the span holds nothing rather than drawing an empty grid in silence', async () => {
        const { transport } = deployment({ events: [] });
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('radio', { name: 'Agenda' }));

        expect(await screen.findByText('Nothing is on the calendar here.')).toBeDefined();
        expect(screen.getByText('Your mail has proposed nothing.')).toBeDefined();
    });
});
