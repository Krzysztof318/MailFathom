// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { StandingView } from '../messageList/listing';
import { ListedMailContext, nothingListed } from '../messageList/useListedMail';
import { AiFiltersShownContext } from '../preferences/aiFilters';
import { AiFilters } from './AiFilters';

function drawing(shown = true, stand: (view: StandingView) => void = () => undefined, folded = false): void {
    render(
        <LocalizationProvider>
            <AiFiltersShownContext value={shown}>
                <ListedMailContext value={{ ...nothingListed, stand }}>
                    <AiFilters folded={folded} />
                </ListedMailContext>
            </AiFiltersShownContext>
        </LocalizationProvider>,
    );
}

describe('AiFilters', () => {
    it('draws the section the design names, under the tree', () => {
        drawing();

        expect(screen.getByRole('region', { name: 'AI filters' })).toBeTruthy();
    });

    it.each([
        ['Needs a decision', 'needsDecision'],
        ['Commitments', 'commitments'],
        ['Deadlines this week', 'deadlinesThisWeek'],
    ] as const)('puts %s in force on the list in front of the reader', (named, view) => {
        const stand = vi.fn<(view: StandingView) => void>();

        drawing(true, stand);
        fireEvent.click(screen.getByRole('button', { name: named }));

        expect(stand).toHaveBeenCalledWith(view);
    });

    // Turned off is the section gone rather than a heading with nothing under it: a heading over three missing entries
    // would say the views had failed, which is a different thing from a reader having asked not to be offered them.
    it('draws nothing at all where the reader has turned the section off', () => {
        drawing(false);

        expect(screen.queryByRole('region', { name: 'AI filters' })).toBeNull();
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('says what pressing an entry will do, the design drawing no lit state to say it afterwards', () => {
        drawing();

        expect(screen.getByRole('button', { name: 'Commitments' }).getAttribute('title')).toBe(
            'AI filter: Commitments',
        );
    });

    it('keeps every entry named in the folded rail, where the names are not drawn', () => {
        drawing(true, () => undefined, true);

        expect(screen.getByRole('button', { name: 'Deadlines this week' })).toBeTruthy();
        expect(screen.queryByText('AI filters')).toBeNull();
    });
});
