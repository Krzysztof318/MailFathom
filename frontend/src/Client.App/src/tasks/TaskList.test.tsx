// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientFailureReason, PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { TaskList } from './TaskList';

// The headings a task stands under are read off the day, so the zone is pinned: a list grouped in whatever zone the
// runner reports would put the same task under a different heading in a different country.
const machineZone = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = machineZone;
});

// A Monday morning, with the week ahead running to the Sunday after it.
const readingAt = Date.parse('2026-09-21T09:00:00+02:00');

function taskDue(id: string, title: string, dueOn: string | null): PersonalTask {
    return { id, title, dueOn, origin: 'Asserted', completed: false, sourceMessageId: null };
}

function drawList({
    tasks = [],
    reading = false,
    paging = false,
    failure = null,
    everythingDone = false,
    selected = [],
    scheduled = [],
    onSelected = vi.fn(),
    onReadMore = vi.fn(),
    onReadAgain = vi.fn(),
}: {
    tasks?: readonly PersonalTask[];
    reading?: boolean;
    paging?: boolean;
    failure?: ClientFailureReason | null;
    everythingDone?: boolean;
    selected?: readonly string[];
    scheduled?: readonly string[];
    onSelected?: (selected: readonly string[]) => void;
    onReadMore?: () => void;
    onReadAgain?: () => void;
} = {}): void {
    process.env['TZ'] = 'Europe/Warsaw';

    render(
        <LocalizationProvider>
            <TaskList
                tasks={tasks}
                reading={reading}
                paging={paging}
                failure={failure}
                everythingDone={everythingDone}
                now={readingAt}
                selected={selected}
                scheduled={scheduled}
                onSelected={onSelected}
                onToggleCompleted={vi.fn()}
                onAccept={vi.fn()}
                onOpenSource={vi.fn()}
                onSchedule={vi.fn()}
                onAskErasure={vi.fn()}
                onReadMore={onReadMore}
                onReadAgain={onReadAgain}
            />
        </LocalizationProvider>,
    );
}

function headings(): (string | null)[] {
    return screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent);
}

describe('TaskList', () => {
    it('groups what is due by when it is due, and draws no heading nothing stands under', () => {
        drawList({
            tasks: [taskDue('a', 'Answer the tender', '2026-09-21'), taskDue('b', 'Send the invoice', '2026-09-25')],
        });

        expect(headings()).toStrictEqual(['Today', 'This week']);
    });

    it('draws each heading with how much stands under it', () => {
        drawList({
            tasks: [taskDue('a', 'Answer the tender', '2026-09-21'), taskDue('b', 'File the return', '2026-09-21')],
        });

        expect(screen.getByText('2')).toBeDefined();
    });

    it('stands a task nobody has dated under the last heading rather than dropping it', () => {
        drawList({ tasks: [taskDue('a', 'Answer the tender', null)] });

        expect(headings()).toStrictEqual(['Later']);
        expect(screen.getByText('Answer the tender')).toBeDefined();
    });

    it('says it is reading before the list has answered', () => {
        drawList({ reading: true });

        expect(screen.getByRole('status').textContent).toBe('Reading…');
    });

    // Drawn under the rows rather than in place of them: somebody who has scrolled to the end of what is read has not
    // left what they were reading.
    it('says a further page is on its way without taking down the rows already read', () => {
        drawList({ tasks: [taskDue('a', 'Answer the tender', '2026-09-21')], paging: true });

        expect(screen.getByRole('status').textContent).toBe('Reading more…');
        expect(screen.getByText('Answer the tender')).toBeDefined();
    });

    it('says why the list did not answer and offers the way out', () => {
        const onReadAgain = vi.fn();

        drawList({ failure: 'unauthorized', onReadAgain });

        expect(screen.getByRole('alert').textContent).toBe('This credential may not read your tasks.');

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(onReadAgain).toHaveBeenCalledOnce();
    });

    it('does not call a list it could not read an empty one', () => {
        drawList({ failure: 'unavailable' });

        expect(screen.queryByText(/Nothing is on the list\./)).toBeNull();
    });

    it('says an empty list is empty, and says what would fill it', () => {
        drawList();

        expect(screen.getByText(/Nothing is on the list\./)).toBeDefined();
    });

    // Two empties rather than one: a reader told the list is empty when everything on it is done and hidden would go
    // looking for work the deployment is holding.
    it('says a list whose every row is done and hidden is that rather than empty', () => {
        drawList({ everythingDone: true });

        expect(screen.getByText('Everything on the list is done. Turn on “Show done” to see it.')).toBeDefined();
    });

    it('asks for the page after the rows already read once a scroll reaches the end of them', () => {
        const onReadMore = vi.fn();

        drawList({ tasks: [taskDue('a', 'Answer the tender', '2026-09-21')], onReadMore });

        // Found by the group's own role and name and then by one hop outwards, which is what
        // `messageList/MessageList.test.tsx` does for the same question: the scroller carries no role of its own, and
        // a query for the utility class that makes it one would pass a renamed class and fail a working screen.
        const scroller = screen.getByRole('region', { name: 'Today' }).parentElement;

        if (scroller === null) {
            throw new Error('The list draws no scroller around its groups.');
        }

        fireEvent.scroll(scroller);

        expect(onReadMore).toHaveBeenCalled();
    });

    it('opens a row’s own menu where a press asked for one, and starts the selection from it', () => {
        const onSelected = vi.fn();

        drawList({ tasks: [taskDue('a', 'Answer the tender', '2026-09-21')], onSelected });

        fireEvent.keyDown(screen.getByRole('listitem'), { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));

        expect(onSelected).toHaveBeenCalledWith(['a']);
    });

    it('puts focus back on the row the menu was opened from once it closes', () => {
        drawList({ tasks: [taskDue('a', 'Answer the tender', '2026-09-21')], onSelected: vi.fn() });

        const row = screen.getByRole('listitem');

        fireEvent.keyDown(row, { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));

        expect(document.activeElement).toBe(row);
    });

    it('adds a row to the selection it is already holding rather than replacing it', () => {
        const onSelected = vi.fn();

        drawList({
            tasks: [taskDue('a', 'Answer the tender', '2026-09-21'), taskDue('b', 'File the return', '2026-09-21')],
            selected: ['a'],
            onSelected,
        });

        fireEvent.click(screen.getByRole('button', { name: 'Select File the return' }));

        expect(onSelected).toHaveBeenCalledWith(['a', 'b']);
    });
});
