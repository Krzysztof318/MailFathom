// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { MoveDestination } from './mailboxDestinations';
import { MoveChoice } from './MoveChoice';

const archive: MoveDestination = { alias: 'work-archive', name: 'Archive' };
const clients: MoveDestination = { alias: 'work-clients', name: 'Projects / Clients' };

// What stands in for the control a strip draws to open this dialog. Named through a value rather than written into
// the markup because the lint rule that keeps copy in the catalogues reads the markup, and this is a test's scaffold
// rather than a sentence anybody reads.
const opener = 'File somewhere';

function Asking({ onChosen }: { readonly onChosen: (destination: MoveDestination) => void }) {
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                {opener}
            </button>

            <MoveChoice asked={asked} destinations={[archive, clients]} onChosen={onChosen} />
        </>
    );
}

function open(onChosen: (destination: MoveDestination) => void = () => undefined): void {
    render(
        <LocalizationProvider>
            <Asking onChosen={onChosen} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: opener }));
}

describe('MoveChoice', () => {
    it('asks which folder rather than whether, filing being reversible and therefore unasked about', () => {
        open();

        expect(screen.getByRole('dialog', { name: 'File in another folder' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Archive' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Projects / Clients' })).toBeDefined();
    });

    it('answers with the folder that was picked, which is what the act is then performed with', () => {
        const chosen = vi.fn();

        open(chosen);
        fireEvent.click(screen.getByRole('button', { name: 'Projects / Clients' }));

        expect(chosen).toHaveBeenCalledWith(clients);
    });

    it('answers with nothing where it was left rather than answered, so nothing is filed by closing it', () => {
        const chosen = vi.fn();

        open(chosen);
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(chosen).not.toHaveBeenCalled();
    });

    // A return value outlives the dialog it was set on, and not every engine clears it on the next opening — so a
    // second opening left alone would file the mail again into the folder the first one picked.
    it('files nothing on a second opening that was left alone, whatever the first one answered', () => {
        const chosen = vi.fn();

        open(chosen);
        fireEvent.click(screen.getByRole('button', { name: 'Archive' }));

        fireEvent.click(screen.getByRole('button', { name: opener }));
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(chosen).toHaveBeenCalledExactlyOnceWith(archive);
    });
});
