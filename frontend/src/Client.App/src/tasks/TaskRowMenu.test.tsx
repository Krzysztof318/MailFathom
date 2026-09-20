// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { TaskRowMenu } from './TaskRowMenu';

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

function menuOver({
    task = taskOf(),
    onSelect = vi.fn(),
    onToggleCompleted = vi.fn(),
    onAccept,
    onOpenSource,
    onSchedule = vi.fn(),
    onAskErasure = vi.fn(),
}: {
    task?: PersonalTask;
    onSelect?: () => void;
    onToggleCompleted?: () => void;
    onAccept?: (() => void) | undefined;
    onOpenSource?: (() => void) | undefined;
    onSchedule?: () => void;
    onAskErasure?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <TaskRowMenu
                task={task}
                at={{ x: 20, y: 30 }}
                onSelect={onSelect}
                onToggleCompleted={onToggleCompleted}
                onAccept={onAccept}
                onOpenSource={onOpenSource}
                onSchedule={onSchedule}
                onAskErasure={onAskErasure}
                onClose={vi.fn()}
            />
        </LocalizationProvider>,
    );
}

function drawn(): (string | null)[] {
    return screen.getAllByRole('menuitem').map((item) => item.textContent);
}

describe('TaskRowMenu', () => {
    it('carries the acts the design draws over a task somebody already owes', () => {
        menuOver({ onOpenSource: vi.fn() });

        expect(drawn()).toStrictEqual([
            'Select tasks',
            'Mark as done',
            'Schedule in the calendar',
            'Open source thread',
            'Delete task',
        ]);
    });

    it('offers to take a proposal, and words the erasure as turning it down rather than as deleting it', () => {
        menuOver({ task: taskOf({ origin: 'Proposed' }), onAccept: vi.fn() });

        expect(drawn()).toContain('Accept');
        expect(drawn()).toContain('Dismiss task');
    });

    it('words the completion by what pressing it would do', () => {
        menuOver({ task: taskOf({ completed: true }) });

        expect(drawn()).toContain('Mark as not done');
    });

    it('offers nothing to schedule for a task with no day to put it on', () => {
        menuOver({ task: taskOf({ dueOn: null }) });

        expect(drawn()).not.toContain('Schedule in the calendar');
    });

    it('offers no way back to a message where the task cites none', () => {
        menuOver();

        expect(drawn()).not.toContain('Open source thread');
    });

    it('starts the selection from its own first item, which is how somebody with no modifier key reaches one', () => {
        const onSelect = vi.fn();

        menuOver({ onSelect });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));

        expect(onSelect).toHaveBeenCalledOnce();
    });

    it('raises the question the erasure stands behind rather than erasing from the menu', () => {
        const onAskErasure = vi.fn();

        menuOver({ onAskErasure });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete task' }));

        expect(onAskErasure).toHaveBeenCalledOnce();
    });

    it('names the task the menu is about', () => {
        menuOver();

        expect(screen.getByText('Answer the tender')).toBeDefined();
    });
});
