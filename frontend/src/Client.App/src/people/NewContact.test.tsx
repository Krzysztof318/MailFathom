// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { NewContact, type ContactDraft } from './NewContact';

// What opens the dialog in the test, named here rather than written into the markup: the localization rule
// holds over every `.tsx` under the packages, and a label a test invented is not a catalogue entry.
const asks = 'Ask';

function Asking({ onSave }: { readonly onSave: (draft: ContactDraft) => void }) {
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

            <NewContact asked={asked} onSave={onSave} />
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

describe('NewContact', () => {
    // A name and an address are what a contact record holds on this deployment; a company and a role are not fields of
    // it, so a form offering them would be asking for values no request could carry.
    it('asks for the two things a record this client writes actually holds', () => {
        drawForm();

        expect(screen.getByLabelText('Name')).toBeDefined();
        expect(screen.getByLabelText('Email address')).toBeDefined();
        expect(screen.queryByLabelText('Company')).toBeNull();
    });

    it('records what was typed once the question has been answered', () => {
        const { saved } = drawForm();

        typeInto('Name', '  Anna Kowalska  ');
        typeInto('Email address', ' anna@contoso.example ');
        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(saved).toHaveBeenCalledWith({ displayName: 'Anna Kowalska', address: 'anna@contoso.example' });
    });

    it('records nothing at all where the question was left rather than answered', () => {
        const { saved } = drawForm();

        typeInto('Name', 'Anna Kowalska');
        typeInto('Email address', 'anna@contoso.example');
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(saved).not.toHaveBeenCalled();
    });

    // Said only once there is something to judge, because a complaint under a field nobody has finished typing in is
    // the client complaining about a state it put them in.
    it('says an address is not one only once something has been typed into the field', () => {
        const { saved } = drawForm();

        typeInto('Name', 'Anna Kowalska');
        expect(screen.queryByRole('alert')).toBeNull();

        typeInto('Email address', 'anna');
        expect(screen.getByRole('alert').textContent).toBe('That does not look like an email address.');

        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));
        expect(saved).not.toHaveBeenCalled();
    });

    it('empties the form on the way out, so the next question is a blank one', () => {
        const { saved } = drawForm();

        typeInto('Name', 'Anna Kowalska');
        typeInto('Email address', 'anna@contoso.example');
        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(saved).toHaveBeenCalledTimes(1);

        fireEvent.click(screen.getByRole('button', { name: asks }));

        expect(screen.getByLabelText<HTMLInputElement>('Name').value).toBe('');
        expect(screen.getByLabelText<HTMLInputElement>('Email address').value).toBe('');
    });
});
