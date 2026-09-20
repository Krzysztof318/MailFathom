// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { longestTaskTitle } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { NewTask, type TaskDraft } from './NewTask';

// What opens the dialog in the test, named here rather than written into the markup: the localization rule holds over
// every `.tsx` under the packages, and a label a test invented is not a catalogue entry.
const asks = 'Ask';

function Asking({ onSave }: { readonly onSave: (draft: TaskDraft) => void }) {
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                {asks}
            </button>

            <NewTask asked={asked} onSave={onSave} />
        </>
    );
}

function drawForm(): { readonly saved: ReturnType<typeof vi.fn> } {
    const saved = vi.fn();

    render(
        <LocalizationProvider>
            <Asking onSave={saved} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: asks }));

    return { saved };
}

function typeInto(field: string, text: string): void {
    fireEvent.change(screen.getByLabelText(field), { target: { value: text } });
}

describe('NewTask', () => {
    // A line and a day are what a task record holds on this deployment; a duration and a reminder are not fields of
    // one, so a form offering them would be asking for values no request could carry.
    it('asks for the two things a record this client writes actually holds', () => {
        drawForm();

        expect(screen.getByLabelText('What has to be done')).toBeDefined();
        expect(screen.getByLabelText('Due day')).toBeDefined();
        expect(screen.queryByLabelText('Reminders')).toBeNull();
    });

    it('records what was typed once the question has been answered', () => {
        const { saved } = drawForm();

        typeInto('What has to be done', '  Answer the tender  ');
        typeInto('Due day', '2026-09-24');
        fireEvent.click(screen.getByRole('button', { name: 'Write it down' }));

        expect(saved).toHaveBeenCalledWith({ title: 'Answer the tender', dueOn: '2026-09-24' });
    });

    it('records a task nobody has named a day for', () => {
        const { saved } = drawForm();

        typeInto('What has to be done', 'Answer the tender');
        fireEvent.click(screen.getByRole('button', { name: 'Write it down' }));

        expect(saved).toHaveBeenCalledWith({ title: 'Answer the tender', dueOn: '' });
    });

    it('records nothing at all where the question was left rather than answered', () => {
        const { saved } = drawForm();

        typeInto('What has to be done', 'Answer the tender');
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(saved).not.toHaveBeenCalled();
    });

    it('refuses to record a line nobody typed', () => {
        drawForm();

        expect(screen.getByRole('button', { name: 'Write it down' }).hasAttribute('disabled')).toBe(true);

        typeInto('What has to be done', '   ');

        expect(screen.getByRole('button', { name: 'Write it down' }).hasAttribute('disabled')).toBe(true);
    });

    it('holds the line to the length the deployment states rather than sending one it would refuse', () => {
        drawForm();

        expect(screen.getByLabelText('What has to be done').getAttribute('maxLength')).toBe(
            longestTaskTitle.toFixed(0),
        );
    });

    it('empties the form on the way out, so a dialog opened again is a blank one', () => {
        const { saved } = drawForm();

        typeInto('What has to be done', 'Answer the tender');
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
        fireEvent.click(screen.getByRole('button', { name: asks }));

        expect(screen.getByLabelText<HTMLInputElement>('What has to be done').value).toBe('');
        expect(saved).not.toHaveBeenCalled();
    });

    it('takes nothing from the reader until the dialog is opened', () => {
        render(
            <LocalizationProvider>
                <Asking onSave={vi.fn()} />
            </LocalizationProvider>,
        );

        expect(document.activeElement).toBe(document.body);
    });
});
