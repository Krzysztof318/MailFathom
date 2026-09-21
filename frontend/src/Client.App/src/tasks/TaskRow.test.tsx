// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { TaskRow } from './TaskRow';

// The day is worded against the reader's own calendar, so the zone is pinned: the same due day read in another one is
// another day, which is the whole of what the short wording has to get right.
const machineZone = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = machineZone;
});

// A Monday morning, which is three days before the day the task below is due.
const readingAt = Date.parse('2026-09-21T09:00:00+02:00');

function taskOf(held: Partial<PersonalTask> = {}): PersonalTask {
    return {
        id: 'a',
        title: 'Answer the tender',
        dueOn: '2026-09-24',
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        ...held,
    };
}

function drawRow({
    task = taskOf(),
    selected = false,
    selecting = false,
    scheduled = false,
    onToggleSelected = vi.fn(),
    onToggleCompleted = vi.fn(),
    onAccept,
    onOpenSource,
    onSchedule = vi.fn(),
    onPress = vi.fn(),
}: {
    task?: PersonalTask;
    selected?: boolean;
    selecting?: boolean;
    scheduled?: boolean;
    onToggleSelected?: () => void;
    onToggleCompleted?: () => void;
    onAccept?: (() => void) | undefined;
    onOpenSource?: (() => void) | undefined;
    onSchedule?: () => void;
    onPress?: (at: { readonly x: number; readonly y: number }) => void;
} = {}): void {
    process.env['TZ'] = 'Europe/Warsaw';

    render(
        <LocalizationProvider>
            <ul>
                <TaskRow
                    task={task}
                    now={readingAt}
                    selected={selected}
                    selecting={selecting}
                    scheduled={scheduled}
                    onToggleSelected={onToggleSelected}
                    onToggleCompleted={onToggleCompleted}
                    onAccept={onAccept}
                    onOpenSource={onOpenSource}
                    onSchedule={onSchedule}
                    onPress={onPress}
                />
            </ul>
        </LocalizationProvider>,
    );
}

// The props a row is drawn with, for the two cases that have to rerender the same row rather than render a second
// one: `drawRow` renders and returns nothing, which is all every other case here needs.
function rowShown({ scheduled }: { readonly scheduled: boolean }) {
    return {
        task: taskOf(),
        now: readingAt,
        selected: false,
        selecting: false,
        scheduled,
        onToggleSelected: vi.fn(),
        onToggleCompleted: vi.fn(),
        onAccept: undefined,
        onOpenSource: undefined,
        onSchedule: vi.fn(),
        onPress: vi.fn(),
    };
}

describe('TaskRow', () => {
    // The spelling is written out rather than compared against a formatter built here, because an expectation built
    // the same way passes for a row that named a zone of its own as happily as for one that did not.
    it('draws the line it is about and writes the day it is due the short way the design does', () => {
        drawRow();

        expect(screen.getByText('Answer the tender')).toBeDefined();
        expect(screen.getByText('09/24')).toBeDefined();
    });

    it('words the reader’s own day rather than numbering it', () => {
        drawRow({ task: taskOf({ dueOn: '2026-09-21' }) });

        expect(screen.getByText('today')).toBeDefined();
    });

    // What a bare `24.09` means is conveyed by where it sits, which is exactly what is not conveyed to somebody
    // listening — so the sentence is there for them whatever is drawn.
    it('says which day that is in a sentence, for a reader who cannot see where it sits', () => {
        drawRow();

        expect(screen.getByText('Due September 24, 2026')).toBeDefined();
    });

    it('says so where nobody has named a day', () => {
        drawRow({ task: taskOf({ dueOn: null }) });

        expect(screen.getByText('No day')).toBeDefined();
    });

    it('marks a task mail proposed, and offers the way back to the message it came out of', () => {
        const onOpenSource = vi.fn();

        drawRow({ task: taskOf({ origin: 'Proposed', sourceMessageId: 'message' }), onOpenSource });

        expect(screen.getByText('from mail')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Open the message this came from' }));

        expect(onOpenSource).toHaveBeenCalledOnce();
    });

    it('offers no way back where the task cites no message', () => {
        drawRow();

        expect(screen.queryByRole('button', { name: 'Open the message this came from' })).toBeNull();
    });

    // What the box does depends on the row it is on, so what it is called has to as well: a screen reader on a row
    // already done would otherwise be told the control marks it done.
    it('names the completion box for what pressing it would do on this row', () => {
        drawRow({ task: taskOf({ completed: true }) });

        expect(screen.getByRole('checkbox', { name: 'Mark Answer the tender as not done' })).toBeDefined();
    });

    it('completes the task from the box the design draws beside it', () => {
        const onToggleCompleted = vi.fn();

        drawRow({ onToggleCompleted });

        fireEvent.click(screen.getByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        expect(onToggleCompleted).toHaveBeenCalledOnce();
    });

    it('offers to put a dated task in the day, and says it is already there once it is', () => {
        const onSchedule = vi.fn();

        drawRow({ onSchedule });

        fireEvent.click(screen.getByRole('button', { name: 'Schedule' }));

        expect(onSchedule).toHaveBeenCalledOnce();
    });

    it('draws the chip rather than the act for a task this screen has already put in the day', () => {
        drawRow({ scheduled: true });

        expect(screen.getByText('In the calendar')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Schedule' })).toBeNull();
    });

    // The act and the chip that replaces it are the same place on the row, so the press removes the control that
    // holds focus. Left alone it falls to the document body and somebody reading with a keyboard is nowhere.
    it('moves focus to the chip when this row’s own act is replaced by it', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        const { rerender } = render(
            <LocalizationProvider>
                <ul>
                    <TaskRow {...rowShown({ scheduled: false })} />
                </ul>
            </LocalizationProvider>,
        );

        const act = screen.getByRole('button', { name: 'Schedule' });

        act.focus();
        fireEvent.click(act);

        rerender(
            <LocalizationProvider>
                <ul>
                    <TaskRow {...rowShown({ scheduled: true })} />
                </ul>
            </LocalizationProvider>,
        );

        expect(document.activeElement).toBe(screen.getByText('In the calendar'));
    });

    // A row scheduled over the whole selection held no focus of its own, so nothing here takes it from wherever the
    // selection bar left it.
    it('takes no focus for a row the selection bar scheduled', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        const { rerender } = render(
            <LocalizationProvider>
                <ul>
                    <TaskRow {...rowShown({ scheduled: false })} />
                </ul>
            </LocalizationProvider>,
        );

        rerender(
            <LocalizationProvider>
                <ul>
                    <TaskRow {...rowShown({ scheduled: true })} />
                </ul>
            </LocalizationProvider>,
        );

        expect(document.activeElement).toBe(document.body);
    });

    it('offers nothing to schedule for a task with no day to put it on', () => {
        drawRow({ task: taskOf({ dueOn: null }) });

        expect(screen.queryByRole('button', { name: 'Schedule' })).toBeNull();
    });

    // The mark is drawn as something to press only while a selection is held, which is what gives somebody with a
    // keyboard a way to add a row to one and what tells a screen reader which rows are in it.
    it('draws no selection mark until a selection is being held', () => {
        drawRow();

        expect(screen.queryByRole('button', { name: 'Select Answer the tender' })).toBeNull();
    });

    it('says whether this row is in the selection, and toggles it from the mark', () => {
        const onToggleSelected = vi.fn();

        drawRow({ selecting: true, selected: true, onToggleSelected });

        const mark = screen.getByRole('button', { name: 'Select Answer the tender' });

        expect(mark.getAttribute('aria-pressed')).toBe('true');

        fireEvent.click(mark);

        expect(onToggleSelected).toHaveBeenCalledOnce();
    });

    it('toggles the row on a modifier-held press with nothing yet selected', () => {
        const onToggleSelected = vi.fn();

        drawRow({ onToggleSelected });

        fireEvent.click(screen.getByText('Answer the tender'), { ctrlKey: true });

        expect(onToggleSelected).toHaveBeenCalledOnce();
    });

    it('toggles the row on a plain press while a selection is held', () => {
        const onToggleSelected = vi.fn();

        drawRow({ selecting: true, onToggleSelected });

        fireEvent.click(screen.getByText('Answer the tender'));

        expect(onToggleSelected).toHaveBeenCalledOnce();
    });

    it('does nothing on a plain press with nothing selected, a task having nowhere to be opened to', () => {
        const onToggleSelected = vi.fn();

        drawRow({ onToggleSelected });

        fireEvent.click(screen.getByText('Answer the tender'));

        expect(onToggleSelected).not.toHaveBeenCalled();
    });

    // A press that landed on one of the row's own controls is that control's act: the box that completes the task
    // would otherwise complete it and pick the row out at once.
    it('leaves a press on one of its own controls to that control', () => {
        const onToggleSelected = vi.fn();

        drawRow({ selecting: true, onToggleSelected });

        fireEvent.click(screen.getByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        expect(onToggleSelected).not.toHaveBeenCalled();
    });

    it.each([
        ['ContextMenu', {}],
        ['F10', { shiftKey: true }],
    ])('opens its own menu from the keyboard with %s', (key, held) => {
        const onPress = vi.fn();

        drawRow({ onPress });

        fireEvent.keyDown(screen.getByRole('listitem'), { key, ...held });

        expect(onPress).toHaveBeenCalledOnce();
    });

    it('offers to take a proposal where one was made, and offers nothing to take otherwise', () => {
        const onAccept = vi.fn();

        drawRow({ task: taskOf({ origin: 'Proposed' }), onAccept });

        fireEvent.click(screen.getByRole('button', { name: 'Accept' }));

        expect(onAccept).toHaveBeenCalledOnce();
    });
});
