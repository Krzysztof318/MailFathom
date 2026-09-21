// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AnswerBlock, BlockEvidence, DeclaredSource, EvidenceEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { EvidenceList } from './EvidenceList';

const agreement: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Master agreement.pdf',
    medium: 'Written',
    unreadable: null,
};

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

const quoted: EvidenceEntry = {
    source: 'c-1',
    fragment: 'Monthly remuneration is EUR 1,200 net',
    relevance: 0.94,
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
};

function listing(entries: readonly EvidenceEntry[]): AnswerBlock {
    return { type: 'evidenceList', named: 'evidenceList', evidence: backed, entries };
}

function renderList(
    block: AnswerBlock,
    {
        sources = [agreement],
        follow = null,
    }: { sources?: readonly DeclaredSource[]; follow?: ((of: string) => void) | null } = {},
) {
    const declared = new Map(sources.map((source) => [source.id, source]));

    return render(
        <LocalizationProvider>
            <AnswerSourcesContext value={{ sources: declared, follow }}>
                <EvidenceList block={block} />
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

describe('EvidenceList', () => {
    beforeEach(() => {
        // The freshness beside an entry is worded against the reader's own day, so the day is pinned rather than
        // taken from whichever one the suite runs on.
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-09-20T18:00:00Z'));
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('names the source and quotes the part of it that matched', () => {
        renderList(listing([quoted]));

        expect(screen.getByText('Master agreement.pdf')).toBeDefined();
        expect(screen.getByText('Monthly remuneration is EUR 1,200 net')).toBeDefined();
    });

    it('says how many messages it holds and what they are ordered by', () => {
        renderList(listing([quoted, { ...quoted, source: 'c-2' }]));

        expect(screen.getByText('2 items · sorted by relevance')).toBeDefined();
    });

    it('says the relevance as a share rather than as a raw number', () => {
        renderList(listing([quoted]));

        expect(screen.getByText('relevance 94%')).toBeDefined();
    });

    it.each([
        ['Current', /^current · /u],
        ['Stale', /^behind · /u],
    ] as const)('says a %s local copy in words rather than leaving a date to be worked out', (staleness, said) => {
        renderList(listing([{ ...quoted, freshness: { staleness, observedAt: '2026-09-20T08:00:00+00:00' } }]));

        expect(screen.getByText(said)).toBeDefined();
    });

    it('says a freshness nothing established rather than drawing a date it does not have', () => {
        renderList(listing([{ ...quoted, freshness: { staleness: 'Unknown', observedAt: null } }]));

        expect(screen.getByText('freshness not established')).toBeDefined();
    });

    it('says what kind of source an entry presents', () => {
        const carried = {
            kind: 'attachment',
            email: '0198f4a1-0000-7000-8000-000000000001',
            attachmentPosition: 0,
        } as const;

        renderList(listing([quoted]), { sources: [{ ...agreement, target: carried }] });

        expect(screen.getByText('attachment')).toBeDefined();
    });

    it('says a source this deployment described rather than read, so it is not taken for a quotation', () => {
        renderList(listing([quoted]), { sources: [{ ...agreement, medium: 'Depicted' }] });

        expect(screen.getByText('description of an image')).toBeDefined();
        expect(screen.getByText(quoted.fragment).tagName).toBe('P');
    });

    it('quotes a written source, so what is drawn as a quotation is one', () => {
        renderList(listing([quoted]));

        expect(screen.getByText(quoted.fragment).tagName).toBe('Q');
    });

    it('draws an entry this client was given no source for as a private source rather than as an omission', () => {
        renderList(listing([quoted]), { sources: [] });

        expect(screen.getByText('private source')).toBeDefined();
        expect(
            screen.getByText(
                'Content not available for review — the conclusion drawn from this source is kept in the result.',
            ),
        ).toBeDefined();
        expect(screen.getByText('relevance 94%')).toBeDefined();
    });

    it('draws no way into a private source, there being nothing this client may open', () => {
        renderList(listing([quoted]), { sources: [], follow: vi.fn() });

        expect(screen.queryByRole('button')).toBeNull();
    });

    it('draws no way into an entry whose source points at a kind this client cannot open', () => {
        renderList(listing([quoted]), { sources: [{ ...agreement, target: null }], follow: vi.fn() });

        expect(screen.queryByRole('button')).toBeNull();
        expect(screen.getByText('Master agreement.pdf')).toBeDefined();
    });

    it('opens the source an entry presents when there is somewhere to open it', () => {
        const follow = vi.fn();
        renderList(listing([quoted]), { follow });

        fireEvent.click(screen.getByRole('button', { name: /Master agreement\.pdf/u }));

        expect(follow).toHaveBeenCalledWith('c-1');
    });

    it('says a list that found nothing found nothing, rather than drawing an empty card', () => {
        renderList(listing([]));

        expect(screen.getByText('No documents met the relevance threshold for this query.')).toBeDefined();
    });

    it('names a block it is not the renderer for rather than drawing somebody else’s data as evidence', () => {
        renderList({ type: null, named: 'RiskScore' });

        expect(screen.getByText('type: RiskScore')).toBeDefined();
    });
});
