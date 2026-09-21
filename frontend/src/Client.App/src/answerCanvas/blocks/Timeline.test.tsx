// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { AnswerBlock, BlockEvidence, DeclaredSource, TimelineEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { ReadingZoneContext } from '../../localization/useReadingZone';
import { AnswerSourcesContext } from '../answerSources';
import { Timeline } from './Timeline';

const agreement: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Master agreement.pdf',
    medium: 'Written',
    unreadable: null,
};

const addendum: DeclaredSource = { ...agreement, id: 'c-2', label: 'SLA addendum.pdf' };

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

const signed: TimelineEntry = {
    occurredAt: '2021-04-12T09:30:00+00:00',
    summary: 'Monthly remuneration set at EUR 1,200',
    subject: 'Master agreement',
    sources: ['c-1'],
};

function chronology(entries: readonly TimelineEntry[], evidence: BlockEvidence = backed): AnswerBlock {
    return { type: 'timeline', named: 'timeline', evidence, entries };
}

function renderTimeline(
    block: AnswerBlock,
    {
        sources = [agreement, addendum],
        follow = null,
        timeZone = null,
    }: {
        sources?: readonly DeclaredSource[];
        follow?: ((of: string) => void) | null;
        timeZone?: string | null;
    } = {},
) {
    const declared = new Map(sources.map((source) => [source.id, source]));

    return render(
        <LocalizationProvider>
            <ReadingZoneContext value={timeZone}>
                <AnswerSourcesContext value={{ sources: declared, follow }}>
                    <Timeline block={block} />
                </AnswerSourcesContext>
            </ReadingZoneContext>
        </LocalizationProvider>,
    );
}

describe('Timeline', () => {
    it('draws each event with what it happened to and what happened', () => {
        renderTimeline(chronology([signed]));

        expect(screen.getByText('Master agreement')).toBeDefined();
        expect(screen.getByText('Monthly remuneration set at EUR 1,200')).toBeDefined();
    });

    it('carries the instant the run sent beside the spelling a reader is shown', () => {
        const { container } = renderTimeline(chronology([signed]));

        expect(container.querySelector('time')?.getAttribute('dateTime')).toBe('2021-04-12T09:30:00+00:00');
    });

    it("places an event on the day the reader's own record states rather than on the sender's", () => {
        renderTimeline(chronology([signed]), { timeZone: 'Pacific/Auckland' });

        expect(screen.getByText('4/12/21, 9:30 PM')).toBeDefined();
    });

    it('says a date it cannot read rather than drawing an empty line where one belongs', () => {
        renderTimeline(chronology([{ ...signed, occurredAt: 'sometime that spring' }]));

        expect(screen.getByText('date not readable')).toBeDefined();
    });

    it('keeps the order the run composed rather than sorting the events by date', () => {
        const later = { ...signed, occurredAt: '2025-11-18T09:30:00+00:00', subject: 'SLA addendum' };

        renderTimeline(chronology([later, signed]));

        expect(screen.getAllByRole('listitem').map((event) => event.textContent)).toEqual([
            expect.stringContaining('SLA addendum'),
            expect.stringContaining('Master agreement'),
        ]);
    });

    it('says how many events it holds', () => {
        renderTimeline(chronology([signed, { ...signed, subject: 'SLA addendum' }]));

        expect(screen.getByText('2 events')).toBeDefined();
    });

    it('numbers a source an event names and the block does not, rather than leaving it uncited', () => {
        renderTimeline(chronology([{ ...signed, sources: ['c-2'] }]), { follow: vi.fn() });

        expect(screen.getByRole('button', { name: 'Citation 2: SLA addendum.pdf' })).toBeDefined();
    });

    it('opens the source an event rests on when there is somewhere to open it', () => {
        const follow = vi.fn();
        renderTimeline(chronology([signed]), { follow });

        fireEvent.click(screen.getByRole('button', { name: 'Citation 1: Master agreement.pdf' }));

        expect(follow).toHaveBeenCalledWith('c-1');
    });

    it('draws an event nothing backs without a citation rather than inventing one', () => {
        renderTimeline(chronology([{ ...signed, sources: [] }], { ...backed, support: 'Unsupported', citations: [] }));

        expect(screen.queryByRole('button')).toBeNull();
    });

    it('says a chronology that found nothing found nothing, rather than drawing an empty card', () => {
        renderTimeline(chronology([], { ...backed, support: 'Unsupported', citations: [] }));

        expect(screen.getByText('No events in the period this question is about.')).toBeDefined();
    });

    it('names a block registered under the wrong type rather than drawing somebody else’s data as a chronology', () => {
        renderTimeline({ type: 'people', named: 'people' });

        expect(screen.getByText('type: people')).toBeDefined();
    });
});
