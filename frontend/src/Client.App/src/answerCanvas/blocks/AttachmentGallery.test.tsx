// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { AnswerBlock, AttachmentEntry, BlockEvidence, DeclaredSource } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { AttachmentGallery } from './AttachmentGallery';

const carried: DeclaredSource = {
    id: 'c-1',
    kind: 'attachment',
    label: 'Master agreement — signatures',
    medium: 'Written',
    unreadable: null,
};

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

const signed: AttachmentEntry = {
    source: 'c-1',
    name: 'Master agreement.pdf',
    mediaType: 'application/pdf',
    sizeOctets: 312_000,
    availability: 'Stored',
};

function gallery(entries: readonly AttachmentEntry[], evidence: BlockEvidence = backed): AnswerBlock {
    return { type: 'attachmentGallery', named: 'attachmentGallery', evidence, entries };
}

function renderGallery(
    block: AnswerBlock,
    {
        sources = [carried],
        follow = null,
    }: { sources?: readonly DeclaredSource[]; follow?: ((of: string) => void) | null } = {},
) {
    const declared = new Map(sources.map((source) => [source.id, source]));

    return render(
        <LocalizationProvider>
            <AnswerSourcesContext value={{ sources: declared, follow }}>
                <AttachmentGallery block={block} />
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

describe('AttachmentGallery', () => {
    it('says what kind of file it is, how large, and which message carried it, before anything is fetched', () => {
        renderGallery(gallery([signed]));

        expect(screen.getByText('pdf')).toBeDefined();
        expect(screen.getByText('Master agreement.pdf')).toBeDefined();
        expect(screen.getByText('312 kB · from Master agreement — signatures')).toBeDefined();
    });

    it('answers the kind from the name where the message declared no type at all', () => {
        renderGallery(gallery([{ ...signed, name: 'Calculation_2027.xlsx', mediaType: null }]));

        expect(screen.getByText('xlsx')).toBeDefined();
    });

    it.each([
        ['Stored', 'available'],
        ['NotStored', 'not stored'],
        ['Removed', 'removed'],
    ] as const)('says a %s file is %s, so a fetch that would fail is never offered', (availability, said) => {
        renderGallery(gallery([{ ...signed, availability }]));

        expect(screen.getByText(said)).toBeDefined();
    });

    it('states the size of a file whose content was never stored, that being the message’s own account of it', () => {
        renderGallery(gallery([{ ...signed, availability: 'NotStored' }]));

        expect(screen.getByText(/312 kB/u)).toBeDefined();
    });

    it('says how many files it holds', () => {
        renderGallery(gallery([signed, { ...signed, name: 'SLA addendum.pdf' }]));

        expect(screen.getByText('2 files')).toBeDefined();
    });

    it('opens the message a file arrived on when there is somewhere to open it', () => {
        const follow = vi.fn();
        renderGallery(gallery([signed]), { follow });

        fireEvent.click(screen.getByRole('button', { name: /Master agreement\.pdf/u }));

        expect(follow).toHaveBeenCalledWith('c-1');
    });

    it('draws no way into a file whose source this client was never given', () => {
        renderGallery(gallery([signed]), { sources: [], follow: vi.fn() });

        expect(screen.queryByRole('button')).toBeNull();
        expect(screen.getByText('Master agreement.pdf')).toBeDefined();
    });

    it('says a gallery that found nothing found nothing, rather than drawing an empty card', () => {
        renderGallery(gallery([], { ...backed, support: 'Unsupported', citations: [] }));

        expect(screen.getByText('Messages in this scope had no attachments.')).toBeDefined();
    });

    it('names a block registered under the wrong type rather than drawing somebody else’s data as files', () => {
        renderGallery({ type: 'people', named: 'people' });

        expect(screen.getByText('type: people')).toBeDefined();
    });
});
