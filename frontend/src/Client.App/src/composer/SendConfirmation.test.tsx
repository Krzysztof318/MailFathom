// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createRef } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { nothingWrittenYet, type Composition } from './composition';
import { SendConfirmation } from './SendConfirmation';

const addressed: Composition = {
    ...nothingWrittenYet('work'),
    subject: 'Invoice',
    to: ['ada@example.invalid'],
    cc: ['bo@example.invalid'],
    bcc: ['auditor@example.invalid'],
    words: 'Here it is.',
};

function drawConfirmation(composition: Composition = addressed, disabled = false): { sent: ReturnType<typeof vi.fn> } {
    const sent = vi.fn();

    render(
        <LocalizationProvider>
            <SendConfirmation
                asked={createRef<HTMLDialogElement>()}
                composition={composition}
                disabled={disabled}
                onSend={sent}
            />
        </LocalizationProvider>,
    );

    return { sent };
}

function ask(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));
}

describe('SendConfirmation', () => {
    it('sends nothing on being pressed, which is what the confirmation is for', () => {
        const { sent } = drawConfirmation();

        ask();

        expect(sent).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog').textContent).toContain('Send this message?');
    });

    it('names every header somebody is written in, the blind copies included', () => {
        drawConfirmation();
        ask();

        const asked = screen.getByRole('dialog').textContent;

        expect(asked).toContain('To ada@example.invalid');
        expect(asked).toContain('Copy to bo@example.invalid');
        expect(asked).toContain('Blind copy to auditor@example.invalid');
        expect(asked).toContain('Subject: Invoice');
    });

    it('names no header nobody is written in', () => {
        drawConfirmation({ ...addressed, cc: [], bcc: [] });
        ask();

        expect(screen.getByRole('dialog').textContent).not.toContain('Copy to');
    });

    it('says the message is addressed to nobody rather than showing three empty headers', () => {
        drawConfirmation({ ...nothingWrittenYet('work'), subject: 'Invoice', words: 'Here it is.' });
        ask();

        const asked = screen.getByRole('dialog').textContent;

        expect(asked).toContain('addressed to nobody');
        expect(asked).toContain('Nobody is addressed');
    });

    it('names what a send would go out without, and offers to send it anyway', () => {
        drawConfirmation(nothingWrittenYet('work'));
        ask();

        const asked = screen.getByRole('dialog').textContent;

        expect(asked).toContain('It goes out without a subject.');
        expect(asked).toContain('It goes out with nothing written in it.');
        expect(screen.getByRole('button', { name: 'Send anyway' })).toBeDefined();
    });

    it('sends once the send is confirmed', () => {
        const { sent } = drawConfirmation();

        ask();
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Send' }));

        expect(sent).toHaveBeenCalledTimes(1);
    });

    it('sends nothing when the way back is taken instead', () => {
        const { sent } = drawConfirmation();

        ask();
        fireEvent.click(screen.getByRole('button', { name: 'Back to writing' }));

        expect(sent).not.toHaveBeenCalled();
    });

    it('offers no send at all while one is already on its way', () => {
        drawConfirmation(addressed, true);

        expect(screen.getByRole('button', { name: 'Send' }).hasAttribute('disabled')).toBe(true);
    });
});
