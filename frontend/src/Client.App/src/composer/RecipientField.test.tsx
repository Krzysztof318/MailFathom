// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { mostRecipientsInOneHeader } from './composition';
import { RecipientField } from './RecipientField';

function drawField(
    addresses: readonly string[] = [],
    completions: readonly string[] = [],
): { changed: ReturnType<typeof vi.fn> } {
    const changed = vi.fn();

    render(
        <LocalizationProvider>
            <RecipientField label="To" addresses={addresses} completions={completions} onChanged={changed} />
        </LocalizationProvider>,
    );

    return { changed };
}

function field(): HTMLElement {
    return screen.getByLabelText('To');
}

describe('RecipientField', () => {
    it('writes an address in when it is finished with Enter', () => {
        const { changed } = drawField();

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).toHaveBeenCalledWith(['ada@example.invalid']);
    });

    it('writes one address in when the next is started with a comma', () => {
        const { changed } = drawField();

        fireEvent.change(field(), { target: { value: 'ada@example.invalid,' } });

        expect(changed).toHaveBeenCalledWith(['ada@example.invalid']);
    });

    it('writes in what was left in the field when the field is left', () => {
        const { changed } = drawField(['bo@example.invalid']);

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.blur(field());

        expect(changed).toHaveBeenCalledWith(['bo@example.invalid', 'ada@example.invalid']);
    });

    it('writes nothing in for an empty field, which is every time somebody tabs past it', () => {
        const { changed } = drawField();

        fireEvent.blur(field());

        expect(changed).not.toHaveBeenCalled();
    });

    it('says why a half-written address was refused rather than doing nothing', () => {
        const { changed } = drawField();

        fireEvent.change(field(), { target: { value: 'ada' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).not.toHaveBeenCalled();
        expect(screen.getByRole('alert').textContent).toContain('That is not an address yet');
    });

    it('says an address is already written here rather than writing it twice', () => {
        const { changed } = drawField(['ada@example.invalid']);

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).not.toHaveBeenCalled();
        expect(screen.getByRole('alert').textContent).toContain('is written here already');
    });

    it('says what one header takes once it is full', () => {
        const full = Array.from({ length: mostRecipientsInOneHeader }, (_, at) => `reader${String(at)}@example.invalid`);
        const { changed } = drawField(full);

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).not.toHaveBeenCalled();
        expect(screen.getByRole('alert').textContent).toContain('at most 256 addresses');
    });

    it('takes one address back off from its own control, named for the address and the header', () => {
        const { changed } = drawField(['ada@example.invalid', 'bo@example.invalid']);

        fireEvent.click(screen.getByRole('button', { name: 'Remove ada@example.invalid from To' }));

        expect(changed).toHaveBeenCalledWith(['bo@example.invalid']);
    });

    it('offers the people already in the conversation to complete from', () => {
        drawField([], ['ada@example.invalid', 'bo@example.invalid']);

        const list = document.querySelector('datalist');

        expect(list).not.toBeNull();
        expect(field().getAttribute('list')).toBe(list?.id);
        expect(list?.querySelectorAll('option').length).toBe(2);
    });

    it('offers no list at all where there is nobody to complete from', () => {
        drawField();

        expect(document.querySelector('datalist')).toBeNull();
        expect(field().hasAttribute('list')).toBe(false);
    });
});
