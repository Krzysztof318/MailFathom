// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AnswerBlock, BlockEvidence, DeclaredSource, PersonEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { ReadingZoneContext } from '../../localization/useReadingZone';
import { AnswerSourcesContext } from '../answerSources';
import { People } from './People';

const agreement: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Contract addendum — signatures',
    medium: 'Written',
    unreadable: null,
};

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

const anna: PersonEntry = {
    person: { displayName: 'Anna Kowalska', address: 'anna@contoso.example' },
    relationship: 'Contoso · waiting for a reply',
    lastContactAt: '2026-09-20T08:47:00+00:00',
    sources: ['c-1'],
};

function found(entries: readonly PersonEntry[]): AnswerBlock {
    return { type: 'people', named: 'people', evidence: backed, entries };
}

function renderPeople(block: AnswerBlock, sources: readonly DeclaredSource[] = [agreement]) {
    const declared = new Map(sources.map((source) => [source.id, source]));

    return render(
        <LocalizationProvider>
            {/* A zone the suite is not running in, so the wording below proves the block placed the instant in the
                zone the reader's record states rather than in whichever one the machine reports. */}
            <ReadingZoneContext value="America/New_York">
                {/* A citation is given somewhere to follow to, which is what the canvas hands every block on the screen
                    this block is drawn on: a chip with nowhere to go says so in its own name, and asserting that here
                    would be asserting `Citation`'s behaviour rather than this block's. */}
                <AnswerSourcesContext value={{ sources: declared, follow: () => undefined }}>
                    <People block={block} />
                </AnswerSourcesContext>
            </ReadingZoneContext>
        </LocalizationProvider>,
    );
}

describe('People', () => {
    beforeEach(() => {
        // The last contact is worded against the reader's own day, so the day is pinned rather than taken from
        // whichever one the suite runs on.
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-09-20T18:00:00Z'));
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('says who somebody is and where they stand rather than listing the messages they wrote', () => {
        renderPeople(found([anna]));

        expect(screen.getByText('Anna Kowalska')).toBeDefined();
        expect(screen.getByText('Contoso · waiting for a reply')).toBeDefined();
    });

    it('says how many people it names and that their roles were read from the mail', () => {
        renderPeople(found([anna, { ...anna, person: { displayName: 'Marta Nowak', address: null } }]));

        expect(screen.getByText('2 people · roles read from the correspondence')).toBeDefined();
    });

    it('carries the citations at the person they were read from, so an assertion about them can be checked', () => {
        renderPeople(found([anna]));

        expect(screen.getByRole('button', { name: 'Citation 1: Contract addendum — signatures' })).toBeDefined();
    });

    it('numbers a citation by its place among the sources the whole block names', () => {
        renderPeople(
            found([
                { ...anna, sources: ['c-2'] },
                { ...anna, person: { displayName: 'Marta Nowak', address: null }, sources: ['c-2'] },
            ]),
            [agreement, { ...agreement, id: 'c-2', label: 'CPI calculation 2027' }],
        );

        // Both rows rest on the same source, so both chips read the same number — which is what makes one number mean
        // one source across the card rather than the first source of whichever row it sits on.
        expect(screen.getAllByRole('button', { name: 'Citation 2: CPI calculation 2027' })).toHaveLength(2);
    });

    it('words the last contact in the zone the reader’s record states rather than spelling a raw instant', () => {
        renderPeople(found([anna]));

        // Written out rather than compared against a formatter built here, because that comparison passes just as
        // happily for a block that named a zone of its own. New York is four hours behind in September, and the contact
        // still falls on the reader's own day, which is what leaves the time alone on the line.
        expect(screen.getByText(/^last contact 4:47/u)).toBeDefined();
    });

    it('says a contact nothing established was not established, rather than leaving the space blank', () => {
        renderPeople(found([{ ...anna, lastContactAt: null }]));

        expect(screen.getByText('no contact established')).toBeDefined();
    });

    it('tells a last contact it could not read apart from one nothing established', () => {
        renderPeople(found([{ ...anna, lastContactAt: 'ostatnio' }]));

        // Saying "no contact established" here would assert about the correspondence what only the date failed to say.
        expect(screen.getByText('last contact date not readable')).toBeDefined();
    });

    it('draws no row as a control, there being nowhere on this canvas to open a person', () => {
        renderPeople(found([anna]));

        // The citation is the one control a row carries, so anything else drawn as a button would be an affordance
        // with no act behind it.
        expect(screen.getAllByRole('button')).toHaveLength(1);
    });

    it('says a block that found nobody found nobody, rather than drawing an empty card', () => {
        renderPeople(found([]));

        expect(screen.getByText('No people could be linked to this question.')).toBeDefined();
    });

    it('names a block it is not the renderer for rather than drawing somebody else’s data as people', () => {
        renderPeople({ type: null, named: 'RiskScore' });

        expect(screen.getByText('type: RiskScore')).toBeDefined();
    });
});
