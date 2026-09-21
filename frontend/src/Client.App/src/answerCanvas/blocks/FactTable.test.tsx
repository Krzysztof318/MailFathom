// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type {
    AnswerBlock,
    BlockEvidence,
    DeclaredSource,
    FactTableCell,
    FactTableColumn,
    FactTableRow,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { FactTable } from './FactTable';

const agreement: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Master agreement.pdf',
    medium: 'Written',
    unreadable: null,
};

const proposal: DeclaredSource = { ...agreement, id: 'c-7', label: '2027 proposal.pdf' };

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

const version: FactTableCell = { value: 'Agreement 2021', sources: [] };
const amount: FactTableCell = { value: 'EUR 1,200', sources: ['c-1'] };
const agreed: FactTableRow = { cells: [version, amount] };

const compared: readonly FactTableColumn[] = ['version', 'amount'];

function comparison(rows: readonly FactTableRow[], columns: readonly FactTableColumn[] = compared): AnswerBlock {
    return { type: 'factTable', named: 'factTable', evidence: backed, columns, rows };
}

function renderTable(block: AnswerBlock, { follow = null }: { follow?: ((of: string) => void) | null } = {}) {
    const declared = new Map([agreement, proposal].map((source) => [source.id, source]));

    return render(
        <LocalizationProvider>
            <AnswerSourcesContext value={{ sources: declared, follow }}>
                <FactTable block={block} />
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

describe('FactTable', () => {
    it('heads each column in the reader’s own language rather than in the run’s identity', () => {
        renderTable(comparison([agreed]));

        expect(screen.getByRole('columnheader', { name: 'Version' })).toBeDefined();
        expect(screen.getByRole('columnheader', { name: 'Amount' })).toBeDefined();
    });

    it('draws the value as the correspondence wrote it rather than reformatting it', () => {
        renderTable(comparison([{ cells: [{ value: 'roughly EUR 40k', sources: [] }, amount] }]));

        expect(screen.getByText('roughly EUR 40k')).toBeDefined();
    });

    it('makes one cell’s evidence reachable rather than the whole table’s', () => {
        const follow = vi.fn();
        renderTable(comparison([agreed]), { follow });

        fireEvent.click(screen.getByRole('button', { name: 'Citation 1: Master agreement.pdf' }));

        // One control for the one cited cell: the cell beside it rests on nothing and offers nothing to press.
        expect(follow).toHaveBeenCalledWith('c-1');
        expect(screen.getAllByRole('button')).toHaveLength(1);
    });

    it('numbers a source a cell names and the block does not, rather than leaving it uncited', () => {
        renderTable(comparison([{ cells: [version, { value: 'EUR 1,568', sources: ['c-7'] }] }]), {
            follow: vi.fn(),
        });

        expect(screen.getByRole('button', { name: 'Citation 2: 2027 proposal.pdf' })).toBeDefined();
    });

    it('says a cell the correspondence is silent about is silent, rather than drawing a blank', () => {
        renderTable(comparison([{ cells: [version, { value: null, sources: [] }] }]));

        expect(screen.getByText('not stated')).toBeDefined();
    });

    it('says how many rows it holds', () => {
        renderTable(comparison([agreed, agreed]));

        expect(screen.getByText('2 rows')).toBeDefined();
    });

    it('scrolls a wide table from the keyboard, so a column past the edge is reachable without a pointer', () => {
        renderTable(comparison([agreed]));

        const scroller = screen.getByRole('region', {
            name: 'Values compared across the columns this answer found',
        });

        expect(scroller.tabIndex).toBe(0);
    });

    it.each([
        ['no row to compare', comparison([])],
        ['no column to compare across', comparison([], [])],
    ])('says a table with %s found nothing, rather than drawing a heading over nothing', (_, block) => {
        renderTable(block);

        expect(screen.getByText('No column had coverage in the sources — the table stays empty.')).toBeDefined();
    });

    it('names a block registered under the wrong type rather than drawing somebody else’s data as a comparison', () => {
        renderTable({ type: 'people', named: 'people' });

        expect(screen.getByText('type: people')).toBeDefined();
    });
});
