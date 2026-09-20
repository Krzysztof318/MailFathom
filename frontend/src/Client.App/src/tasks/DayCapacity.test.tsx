// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { DayLayout, PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { DayCapacity } from './DayCapacity';

// A zone pinned for one case is put back for the reason a fake clock is released: it is the worker's, not the file's.
const machineZone = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = machineZone;
});

function taskCalled(id: string, title: string): PersonalTask {
    return { id, title, dueOn: '2026-09-21', origin: 'Asserted', completed: false, sourceMessageId: null };
}

const arranged: DayLayout = {
    outcome: 'Arranged',
    placements: [{ taskId: 'a', startAt: '2026-09-21T09:00:00+00:00', minutes: 45 }],
    notToday: ['b'],
};

function drawPanel({
    dueToday = 3,
    eventsToday = 2,
    offered = true,
    arranging = false,
    layout = null,
    placed = [taskCalled('a', 'Answer the tender')],
    applying = false,
    onArrange = vi.fn(),
    onApply = vi.fn(),
    onPutDown = vi.fn(),
}: {
    dueToday?: number;
    eventsToday?: number;
    offered?: boolean;
    arranging?: boolean;
    layout?: DayLayout | null;
    placed?: readonly PersonalTask[];
    applying?: boolean;
    onArrange?: () => void;
    onApply?: () => void;
    onPutDown?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <DayCapacity
                dueToday={dueToday}
                eventsToday={eventsToday}
                offered={offered}
                arranging={arranging}
                layout={layout}
                placed={placed}
                applying={applying}
                onArrange={onArrange}
                onApply={onApply}
                onPutDown={onPutDown}
            />
        </LocalizationProvider>,
    );
}

describe('DayCapacity', () => {
    it('counts the day rather than composing a sentence about it', () => {
        drawPanel();

        expect(screen.getByText(/3 tasks are due today\./)).toBeDefined();
        expect(screen.getByText(/The calendar already holds 2 events\./)).toBeDefined();
    });

    it('counts one of each in the form English has for one', () => {
        drawPanel({ dueToday: 1, eventsToday: 1 });

        expect(screen.getByText(/One task is due today\./)).toBeDefined();
        expect(screen.getByText(/The calendar already holds one event\./)).toBeDefined();
    });

    it('offers the act where the deployment arranges a day and this credential may ask', () => {
        const onArrange = vi.fn();

        drawPanel({ onArrange });

        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        expect(onArrange).toHaveBeenCalledOnce();
    });

    // Absent rather than inert: a control that could never answer says less about why than not drawing it does.
    it('offers nothing to arrange where the deployment arranges no day', () => {
        drawPanel({ offered: false });

        expect(screen.queryByRole('button', { name: 'Lay out today' })).toBeNull();
    });

    it('says it is arranging while the arrangement is on its way', () => {
        drawPanel({ arranging: true });

        expect(screen.getByRole('status').textContent).toBe('Laying out the rest of the day…');
        expect(screen.queryByRole('button', { name: 'Lay out today' })).toBeNull();
    });

    it('draws each placement with the line it is about and the hour it suggests', () => {
        process.env['TZ'] = 'UTC';

        drawPanel({ layout: arranged });

        expect(screen.getByText('Answer the tender')).toBeDefined();
        expect(screen.getByText('09:00 AM')).toBeDefined();
    });

    it('names a placement whose task is no longer on the list rather than drawing an empty row', () => {
        drawPanel({ layout: arranged, placed: [] });

        expect(screen.getByText('A task that is no longer on the list')).toBeDefined();
    });

    it('says how much did not fit what is left of the day', () => {
        drawPanel({ layout: arranged });

        expect(screen.getByText('One task did not fit what is left of today.')).toBeDefined();
    });

    // The acceptance the panel exists for: the calendar is written by the control that says so and by nothing else.
    it('writes nothing until the offer is accepted, and puts it down without writing anything', () => {
        const onApply = vi.fn();
        const onPutDown = vi.fn();

        drawPanel({ layout: arranged, onApply, onPutDown });

        fireEvent.click(screen.getByRole('button', { name: 'Leave it' }));

        expect(onPutDown).toHaveBeenCalledOnce();
        expect(onApply).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('button', { name: 'Add to calendar' }));

        expect(onApply).toHaveBeenCalledOnce();
    });

    it('says the calendar is being written while it is', () => {
        drawPanel({ layout: arranged, applying: true });

        expect(screen.getByRole('button', { name: 'Adding…' })).toBeDefined();
    });

    it('says the provider allowance is spent rather than reporting a failure', () => {
        drawPanel({ layout: { outcome: 'AllowanceSpent', placements: [], notToday: [] } });

        expect(screen.getByRole('status').textContent).toBe(
            'This deployment has spent what it may ask a provider for today. Try again tomorrow.',
        );
    });

    it('says an arrangement that placed nothing placed nothing', () => {
        drawPanel({ layout: { outcome: 'NotArranged', placements: [], notToday: [] } });

        expect(screen.getByRole('status').textContent).toBe(
            'There was nothing left to lay out in what remains of today.',
        );
    });
});
