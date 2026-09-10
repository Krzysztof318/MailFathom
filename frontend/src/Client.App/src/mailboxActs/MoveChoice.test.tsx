// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { FolderMaintenanceContext, noFolderMaintenance, type FolderMaintenance } from '../folders/useFolderMaintenance';
import { LocalizationProvider } from '../localization/Localization';
import type { MoveDestination, MoveDestinationGroup } from './mailboxDestinations';
import { MoveChoice } from './MoveChoice';

const archive: MoveDestination = { alias: 'work-archive', name: 'INBOX.Archiwum', role: 'Archive' };
const clients: MoveDestination = { alias: 'work-clients', name: 'Projects / Clients', role: null };

const work: MoveDestinationGroup = {
    accountId: 'work',
    accountName: 'Northwind \u00b7 work',
    ordinal: 0,
    destinations: [archive, clients],
};

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

            <MoveChoice asked={asked} groups={[work]} onChosen={onChosen} />
        </>
    );
}

function open(
    onChosen: (destination: MoveDestination) => void = () => undefined,
    maintenance: FolderMaintenance = noFolderMaintenance,
): void {
    render(
        <LocalizationProvider>
            <FolderMaintenanceContext value={maintenance}>
                <Asking onChosen={onChosen} />
            </FolderMaintenanceContext>
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: opener }));
}

/** A client whose credential may make a folder, recording the mailbox each act was asked about. */
function offering(): { maintenance: FolderMaintenance; asked: unknown[] } {
    const asked: unknown[] = [];

    return {
        asked,
        maintenance: {
            ...noFolderMaintenance,
            offered: true,
            declare: (mailbox, parent) => asked.push({ mailbox, parent }),
        },
    };
}

describe('MoveChoice', () => {
    it('asks which folder rather than whether, filing being reversible and therefore unasked about', () => {
        open();

        expect(screen.getByRole('dialog', { name: 'File in another folder' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Archive' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Projects / Clients' })).toBeDefined();
    });

    it('gathers the folders under the account they belong to, rather than as one list of folders from nowhere', () => {
        open();

        const group = screen.getByRole('region', { name: work.accountName });

        expect(group).toBeDefined();
        expect(screen.getByRole('button', { name: 'Archive' }).closest('section')).toBe(group);
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

describe('MoveChoice, making a folder', () => {
    it('offers a folder that does not exist yet in the mailbox the group stands for', () => {
        const { maintenance, asked } = offering();

        open(() => undefined, maintenance);
        fireEvent.click(screen.getByRole('button', { name: 'New folder here' }));

        expect(asked).toEqual([
            {
                mailbox: {
                    accountId: 'work',
                    accountName: work.accountName,
                    declaredAliases: ['work-archive', 'work-clients'],
                },
                parent: null,
            },
        ]);
    });

    it('leaves the sheet before the dialog opens, two questions at once being one nobody can read', () => {
        const { maintenance } = offering();

        open(() => undefined, maintenance);
        fireEvent.click(screen.getByRole('button', { name: 'New folder here' }));

        expect(screen.queryByRole('dialog', { name: 'File in another folder' })).toBeNull();
    });

    it('files nothing when the sheet is left that way, the folder not being a destination yet', () => {
        const chosen = vi.fn();
        const { maintenance } = offering();

        open(chosen, maintenance);
        fireEvent.click(screen.getByRole('button', { name: 'New folder here' }));

        expect(chosen).not.toHaveBeenCalled();
    });

    it('offers nothing of the kind to a credential that may not change what the deployment reads', () => {
        open();

        expect(screen.queryByRole('button', { name: 'New folder here' })).toBeNull();
    });
});
