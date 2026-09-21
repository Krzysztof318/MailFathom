// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { TasksSpace } from './TasksSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// jsdom evaluates no media query, so the composition a test is about is stated as a width and read back out of the
// query itself rather than from a table this file would have to keep in step with the stylesheet. It is the double
// `people/PeopleSpace.test.tsx` uses, for the same reason.
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

const machineZone = process.env['TZ'];

beforeEach(() => {
    atWidth(desktop);
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = machineZone;

    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

function taskCalled(id: string, title: string, held: Record<string, unknown> = {}): unknown {
    return {
        id,
        title,
        dueOn: '2026-09-24',
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        ...held,
    };
}

const answered = { status: 200, headers: {} } as const;

// One deployment behind the screen: both halves of the list, the day beside it, whether a day is arranged at all, the
// arrangement itself, and the five writes. A test states what each answers and reads back what the screen did about it.
function deployment({
    committed = [taskCalled('a', 'Answer the tender')],
    proposed = [],
    events = [],
    arranges = true,
    layout = { arranged: false, placements: [], notToday: [] },
    layoutStatus = 200,
    refuses = [],
    holdsCalendarWrites = false,
}: {
    committed?: readonly unknown[];
    proposed?: readonly unknown[];
    events?: readonly unknown[];
    arranges?: boolean;
    layout?: unknown;
    layoutStatus?: number;

    /** Which writes this deployment will not perform, which is how a batch is made to half succeed. */
    refuses?: readonly string[];

    /** Whether a write to the calendar is left outstanding, which is how a second press is caught in flight. */
    holdsCalendarWrites?: boolean;
} = {}): {
    readonly transport: MailFathomTransport;
    readonly sent: () => readonly ClientRequest[];
} {
    const sent: ClientRequest[] = [];
    let served = 0;

    return {
        sent: () => sent,
        transport: (request) => {
            sent.push(request);

            const { method, path } = request;
            let answer: ClientResponse;

            if (path.includes('/tasks/today/layout')) {
                answer =
                    method === 'GET'
                        ? { ...answered, body: JSON.stringify({ arrangesDays: arranges }) }
                        : { status: layoutStatus, headers: {}, body: JSON.stringify(layout) };
            } else if (path.includes('/calendar')) {
                if (method === 'POST' && holdsCalendarWrites) {
                    return new Promise<ClientResponse>(() => undefined);
                }

                answer =
                    method === 'POST'
                        ? {
                              ...answered,
                              body: JSON.stringify({
                                  id: 'written',
                                  title: 'Answer the tender',
                                  start: '2026-09-23T22:00:00.000Z',
                                  end: '2026-09-23T22:00:00.000Z',
                                  isAllDay: false,
                                  reminders: [],
                                  remindsAt: [],
                                  origin: 'Asserted',
                                  sourceMessage: null,
                                  recordedAt: '2026-09-21T07:00:00+00:00',
                                  amendedAt: '2026-09-21T07:00:00+00:00',
                              }),
                          }
                        : { ...answered, body: JSON.stringify({ events }) };
            } else if (method === 'DELETE') {
                answer = refuses.some((task) => path.endsWith(`/tasks/${task}`))
                    ? { status: 503, headers: {}, body: '' }
                    : { ...answered, body: JSON.stringify({ id: 'a', erased: true }) };
            } else if (method === 'POST') {
                answer = refuses.some((task) => path.includes(`/tasks/${task}`))
                    ? { status: 503, headers: {}, body: '' }
                    : { ...answered, body: JSON.stringify(taskCalled('a', 'Answer the tender')) };
            } else if (path.includes('/tasks/proposed')) {
                served += 1;
                answer = { ...answered, body: JSON.stringify({ tasks: served > 1 ? [] : proposed, nextCursor: null }) };
            } else {
                answer = { ...answered, body: JSON.stringify({ tasks: committed, nextCursor: null }) };
            }

            return Promise.resolve(answer);
        },
    };
}

function drawSpace(
    transport: MailFathomTransport,
    {
        asksDeployment = true,
        onOpenMessage = vi.fn(),
    }: { asksDeployment?: boolean; onOpenMessage?: (id: string) => void } = {},
): void {
    render(
        <LocalizationProvider>
            <TasksSpace
                session={session}
                transport={transport}
                asksDeployment={asksDeployment}
                onOpenMessage={onOpenMessage}
            />
        </LocalizationProvider>,
    );
}

function pathsSent(sent: readonly ClientRequest[], method: string): string[] {
    return sent.filter((request) => request.method === method).map((request) => request.path);
}

describe('TasksSpace', () => {
    it('draws both halves of the list as one, marking what came out of mail', async () => {
        const { transport } = deployment({
            proposed: [taskCalled('b', 'Send the invoice', { origin: 'Proposed', sourceMessageId: 'message' })],
        });

        drawSpace(transport);

        expect(await screen.findByText('Answer the tender')).toBeDefined();
        expect(screen.getByText('Send the invoice')).toBeDefined();
        expect(screen.getByText('from mail')).toBeDefined();
    });

    it('reads today’s events through the calendar’s own route and draws them beside the list', async () => {
        const { transport, sent } = deployment({
            events: [
                {
                    id: 'e',
                    title: 'Standup',
                    start: '2026-09-21T07:00:00+00:00',
                    end: '2026-09-21T07:15:00+00:00',
                    isAllDay: false,
                    reminders: [],
                    remindsAt: [],
                    origin: 'Asserted',
                    sourceMessage: null,
                    recordedAt: '2026-09-21T07:00:00+00:00',
                    amendedAt: '2026-09-21T07:00:00+00:00',
                },
            ],
        });

        drawSpace(transport);

        expect(await screen.findByText('Standup')).toBeDefined();
        expect(pathsSent(sent(), 'GET').some((path) => path.includes('/calendar?from='))).toBe(true);
    });

    // Through child 2's route, and the entry goes because the list is read again rather than corrected in place.
    it('takes a task mail proposed, and reads the list again rather than editing it', async () => {
        const { transport, sent } = deployment({
            committed: [],
            proposed: [taskCalled('b', 'Send the invoice', { origin: 'Proposed' })],
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Accept' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Added to your tasks.');
        });

        expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/b/acceptance'))).toBe(true);
        expect(screen.queryByText('Send the invoice')).toBeNull();
    });

    it('turns a proposal down through the erasure, once the question standing behind it is answered', async () => {
        const { transport, sent } = deployment({
            committed: [],
            proposed: [taskCalled('b', 'Send the invoice', { origin: 'Proposed' })],
        });

        drawSpace(transport);

        fireEvent.keyDown(await screen.findByRole('listitem'), { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Dismiss task' }));

        expect(screen.getByText('Delete this task?')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        await waitFor(() => {
            expect(pathsSent(sent(), 'DELETE').some((path) => path.endsWith('/tasks/b'))).toBe(true);
        });

        await waitFor(() => {
            expect(screen.queryByText('Send the invoice')).toBeNull();
        });
    });

    it('marks a task done and says so', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Marked as done.');
        });

        expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/a/completion'))).toBe(true);
    });

    // A task states a day rather than a time, so what is written claims no hours and this client invents none.
    it('puts a task in the calendar on the day it is due, with no hours claimed', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Schedule' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Put in the calendar.');
        });

        const written = sent().find((request) => request.method === 'POST' && request.path.endsWith('/calendar'));

        expect(JSON.parse(written?.body ?? 'null')).toStrictEqual({
            title: 'Answer the tender',
            start: '2026-09-23T22:00:00.000Z',
            end: null,
            isAllDay: true,
            reminders: [],
            sourceMessage: null,
        });
    });

    // A second press while the first batch is outstanding would write the same day-long event again, and the control
    // stays pressable for as long as the calendar takes to answer.
    it('writes the calendar once however many times the act is pressed while it is outstanding', async () => {
        const { transport, sent } = deployment({ holdsCalendarWrites: true });

        drawSpace(transport);

        const act = await screen.findByRole('button', { name: 'Schedule' });

        fireEvent.click(act);

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Putting it in the calendar…');
        });

        fireEvent.click(act);
        fireEvent.click(act);

        expect(
            sent().filter((request) => request.method === 'POST' && request.path.endsWith('/calendar')),
        ).toHaveLength(1);
    });

    // Which of the four it was decides what somebody does next, so a batch the deployment refused outright says it
    // rather than leaving them with a sentence that fits every failure equally.
    it('names why the deployment refused where it refused the whole of it', async () => {
        const { transport } = deployment({ refuses: ['a'] });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe(
                'The deployment did not accept that: unavailable. Nothing was changed.',
            );
        });
    });

    it('says there is nothing to put in the calendar where nothing picked out names a day', async () => {
        const { transport } = deployment({ committed: [taskCalled('a', 'Answer the tender', { dueOn: null })] });

        drawSpace(transport);

        fireEvent.keyDown(await screen.findByRole('listitem'), { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));
        fireEvent.click(screen.getByRole('button', { name: 'Schedule in the calendar' }));

        await waitFor(() => {
            expect(
                screen.getByText('Nothing here names a day, so there is nothing to put in the calendar.'),
            ).toBeDefined();
        });
    });

    // The acceptance the screen exists for: the arrangement is an offer and the calendar is written by the control
    // that says so.
    it('draws the arrangement the deployment suggests and writes nothing until it is accepted', async () => {
        const { transport, sent } = deployment({
            layout: {
                arranged: true,
                placements: [{ taskId: 'a', startAt: '2026-09-21T09:00:00+00:00', minutes: 45 }],
                notToday: [],
            },
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Lay out today' }));

        await waitFor(() => {
            expect(screen.getByRole('button', { name: 'Add to calendar' })).toBeDefined();
        });

        expect(sent().some((request) => request.method === 'POST' && request.path.endsWith('/calendar'))).toBe(false);

        fireEvent.click(screen.getByRole('button', { name: 'Add to calendar' }));

        await waitFor(() => {
            expect(sent().some((request) => request.method === 'POST' && request.path.endsWith('/calendar'))).toBe(
                true,
            );
        });

        const written = sent().find((request) => request.method === 'POST' && request.path.endsWith('/calendar'));

        expect(JSON.parse(written?.body ?? 'null')).toStrictEqual({
            title: 'Answer the tender',
            start: '2026-09-21T09:00:00+00:00',
            end: '2026-09-21T09:45:00.000Z',
            isAllDay: false,
            reminders: [],
            sourceMessage: null,
        });
    });

    it('puts an arrangement down without writing any of it', async () => {
        const { transport, sent } = deployment({
            layout: {
                arranged: true,
                placements: [{ taskId: 'a', startAt: '2026-09-21T09:00:00+00:00', minutes: 45 }],
                notToday: [],
            },
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Lay out today' }));

        fireEvent.click(await screen.findByRole('button', { name: 'Leave it' }));

        expect(sent().some((request) => request.method === 'POST' && request.path.endsWith('/calendar'))).toBe(false);
        expect(screen.getByRole('button', { name: 'Lay out today' })).toBeDefined();
    });

    it('offers no arrangement where the deployment arranges no day', async () => {
        const { transport } = deployment({ arranges: false });

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        expect(screen.queryByRole('button', { name: 'Lay out today' })).toBeNull();
    });

    it('asks nothing about arranging a day where this credential may not ask the deployment anything', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport, { asksDeployment: false });

        await screen.findByText('Answer the tender');

        expect(pathsSent(sent(), 'GET').some((path) => path.endsWith('/tasks/today/layout'))).toBe(false);
        expect(screen.queryByRole('button', { name: 'Lay out today' })).toBeNull();
    });

    it('says the provider allowance is spent rather than reporting a failure', async () => {
        const { transport } = deployment({ layoutStatus: 429, layout: {} });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Lay out today' }));

        await waitFor(() => {
            expect(
                screen.getByText('This deployment has spent what it may ask a provider for today. Try again tomorrow.'),
            ).toBeDefined();
        });
    });

    it('replaces the toolbar with the selection bar while tasks are picked out, and leaves it through its own control', async () => {
        const { transport } = deployment();

        drawSpace(transport);

        fireEvent.keyDown(await screen.findByRole('listitem'), { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));

        expect(screen.getByRole('toolbar', { name: 'Acts over the tasks you picked out' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Show done' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Cancel selection' }));

        expect(screen.getByRole('button', { name: 'Show done' })).toBeDefined();
    });

    it('confirms a destructive act over several rows before performing any of it', async () => {
        const { transport, sent } = deployment({
            committed: [taskCalled('a', 'Answer the tender'), taskCalled('b', 'File the return')],
        });

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        fireEvent.keyDown(screen.getAllByRole('listitem')[0] as Element, { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));
        fireEvent.click(screen.getByRole('button', { name: 'Select File the return' }));
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(screen.getByText('Delete 2 tasks?')).toBeDefined();
        expect(pathsSent(sent(), 'DELETE')).toStrictEqual([]);
    });

    it('counts what the deployment refused in a batch rather than calling the whole of it a failure', async () => {
        const { transport } = deployment({
            committed: [taskCalled('a', 'Answer the tender'), taskCalled('b', 'File the return')],
            refuses: ['b'],
        });

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        fireEvent.keyDown(screen.getAllByRole('listitem')[0] as Element, { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));
        fireEvent.click(screen.getByRole('button', { name: 'Select File the return' }));
        fireEvent.click(screen.getByRole('button', { name: 'Mark as done' }));

        await waitFor(() => {
            expect(screen.getByText('The deployment refused one of these. The rest went through.')).toBeDefined();
        });
    });

    it('hides what is done until the toolbar is asked to show it', async () => {
        const { transport } = deployment({
            committed: [taskCalled('a', 'Answer the tender', { completed: true })],
        });

        drawSpace(transport);

        await waitFor(() => {
            expect(screen.getByText('Everything on the list is done. Turn on “Show done” to see it.')).toBeDefined();
        });

        fireEvent.click(screen.getByRole('button', { name: 'Show done' }));

        expect(screen.getByText('Answer the tender')).toBeDefined();
    });

    it('writes a task down from the form, as one the person asserted', async () => {
        const { transport, sent } = deployment({ committed: [] });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'New task' }));
        fireEvent.change(screen.getByLabelText('What has to be done'), {
            target: { value: 'Answer the tender' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Write it down' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Written down.');
        });

        const written = sent().find((request) => request.method === 'POST' && request.path.endsWith('/tasks'));

        expect(JSON.parse(written?.body ?? 'null')).toStrictEqual({
            title: 'Answer the tender',
            dueOn: null,
            sourceMessageId: null,
        });
    });

    // A task cites a message and mail is read in the Mail space, so opening one is the frame's act rather than this
    // screen's.
    it('hands the message a task cites to the frame rather than drawing it here', async () => {
        const onOpenMessage = vi.fn();
        const { transport } = deployment({
            committed: [taskCalled('a', 'Answer the tender', { sourceMessageId: 'message' })],
        });

        drawSpace(transport, { onOpenMessage });

        fireEvent.click(await screen.findByRole('button', { name: 'Open the message this came from' }));

        expect(onOpenMessage).toHaveBeenCalledWith('message');
    });

    it('offers the way to write a task down at a phone width too, where the toolbar has nowhere to stand', async () => {
        atWidth(phone);

        const { transport } = deployment();

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        expect(screen.getByRole('button', { name: 'New task' })).toBeDefined();
    });

    // The sidebar stands beside the list where there is room and under it where there is not, and the difference
    // between those two is a stylesheet's. What a test here can say is the part that would be a defect either way:
    // that both panels are still in the document at the narrowest composition rather than dropped out of it.
    it('keeps both sidebar panels reachable at a phone width rather than dropping them', async () => {
        atWidth(phone);

        const { transport } = deployment();

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        expect(screen.getByRole('heading', { name: "Today's calendar" })).toBeDefined();
        expect(screen.getByRole('heading', { name: 'Day capacity' })).toBeDefined();
    });
});
