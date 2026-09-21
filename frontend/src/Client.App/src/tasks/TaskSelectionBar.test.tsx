// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { TaskSelectionBar } from './TaskSelectionBar';

function taskCalled(id: string, title: string): PersonalTask {
    return {
        id,
        title,
        dueOn: '2026-09-24',
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
    };
}

const picked = [taskCalled('a', 'Answer the tender'), taskCalled('b', 'Send the invoice')];

function drawBar({
    selected = picked,
    onClear = vi.fn(),
    onComplete = vi.fn(),
    onSchedule = vi.fn(),
    onAskErasure = vi.fn(),
}: {
    selected?: readonly PersonalTask[];
    onClear?: () => void;
    onComplete?: () => void;
    onSchedule?: () => void;
    onAskErasure?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <TaskSelectionBar
                    selected={selected}
                    onClear={onClear}
                    onComplete={onComplete}
                    onSchedule={onSchedule}
                    onAskErasure={onAskErasure}
                />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

describe('TaskSelectionBar', () => {
    it('says how many tasks are picked out, where the toolbar would otherwise stand', () => {
        drawBar();

        expect(screen.getByRole('status').textContent).toBe('2 selected');
    });

    it('is a toolbar with a name, so a reader arriving at it is told what it is about', () => {
        drawBar();

        expect(screen.getByRole('toolbar', { name: 'Acts over the tasks you picked out' })).toBeDefined();
    });

    it('carries the three acts the design draws, each named to a screen reader', () => {
        drawBar();

        for (const act of ['Mark as done', 'Schedule in the calendar', 'Delete']) {
            expect(screen.getByRole('button', { name: act })).toBeDefined();
        }
    });

    it('leaves the selection through its own control, which acts on nothing', () => {
        const onClear = vi.fn();
        const onAskErasure = vi.fn();

        drawBar({ onClear, onAskErasure });

        fireEvent.click(screen.getByRole('button', { name: 'Cancel selection' }));

        expect(onClear).toHaveBeenCalledOnce();
        expect(onAskErasure).not.toHaveBeenCalled();
    });

    it('marks everything picked out as done', () => {
        const onComplete = vi.fn();

        drawBar({ onComplete });

        fireEvent.click(screen.getByRole('button', { name: 'Mark as done' }));

        expect(onComplete).toHaveBeenCalledOnce();
    });

    it('puts everything picked out in the calendar', () => {
        const onSchedule = vi.fn();

        drawBar({ onSchedule });

        fireEvent.click(screen.getByRole('button', { name: 'Schedule in the calendar' }));

        expect(onSchedule).toHaveBeenCalledOnce();
    });

    // The bar raises the question rather than performing the act, which is what the confirmation over several rows is
    // for: the erasure is destructive and permanent.
    it('raises the question the erasure stands behind rather than erasing from the bar', () => {
        const onAskErasure = vi.fn();

        drawBar({ onAskErasure });

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(onAskErasure).toHaveBeenCalledOnce();
    });
});
