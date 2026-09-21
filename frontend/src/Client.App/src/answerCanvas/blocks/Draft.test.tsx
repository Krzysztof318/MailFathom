// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AnswerBlock, BlockEvidence, ComposedDraft, DeclaredSource, DraftDisposition } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { Draft } from './Draft';

const addendum: DeclaredSource = {
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

const composed: ComposedDraft = {
    recipients: [{ displayName: 'Anna Kowalska', address: 'anna@contoso.example' }],
    subject: 'Re: proposed terms for 2027',
    body: 'We accept shortening the response time to 2 hours on business days.',
    disposition: 'Composed',
};

function drafted(disposition: DraftDisposition = 'Composed'): AnswerBlock {
    return { type: 'draft', named: 'draft', evidence: backed, draft: { ...composed, disposition } };
}

function renderDraft(block: AnswerBlock) {
    const declared = new Map([[addendum.id, addendum]]);

    return render(
        <LocalizationProvider>
            {/* A citation is given somewhere to follow to, as the canvas gives every block one: a chip with nowhere to
                go says so in its own name, which is `Citation`'s behaviour rather than this block's. */}
            <AnswerSourcesContext value={{ sources: declared, follow: () => undefined }}>
                <Draft block={block} />
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

describe('Draft', () => {
    it('shows who it is addressed to, what it is about, and what it says', () => {
        renderDraft(drafted());

        expect(screen.getByText('Anna Kowalska (anna@contoso.example)')).toBeDefined();
        expect(screen.getByText('Re: proposed terms for 2027')).toBeDefined();
        expect(screen.getByText('We accept shortening the response time to 2 hours on business days.')).toBeDefined();
    });

    it('shows the address beside the name, which is what somebody about to send is checking', () => {
        renderDraft({
            type: 'draft',
            named: 'draft',
            evidence: backed,
            draft: {
                ...composed,
                recipients: [
                    { displayName: 'Anna Kowalska', address: 'anna@contoso.example' },
                    { displayName: 'Anna Kowalska', address: 'a.kowalska@fabrikam.example' },
                ],
            },
        });

        expect(
            screen.getByText(
                'Anna Kowalska (anna@contoso.example) and Anna Kowalska (a.kowalska@fabrikam.example)',
            ),
        ).toBeDefined();
    });

    it('names a recipient the correspondence gave no name beside by their address alone', () => {
        renderDraft({
            type: 'draft',
            named: 'draft',
            evidence: backed,
            draft: {
                ...composed,
                recipients: [{ displayName: 'anna@contoso.example', address: 'anna@contoso.example' }],
            },
        });

        expect(screen.getByText('anna@contoso.example')).toBeDefined();
    });

    it.each([
        ['Composed', 'Local draft — nothing was sent'],
        ['Saved', 'Saved to your drafts — nothing was sent'],
        ['Queued', 'Waiting in the outbox — nothing has been sent yet'],
    ] as const)('says what has become of a %s draft, and that nothing left', (disposition, said) => {
        renderDraft(drafted(disposition));

        expect(screen.getByText(said)).toBeDefined();
    });

    it('offers no way to send it, sending belonging to the surface that governs sending', () => {
        renderDraft(drafted());

        const named = screen
            .getAllByRole('button')
            .map((control) => control.getAttribute('aria-label') ?? control.textContent);

        expect(named).toEqual(['Citation 1: Contract addendum — signatures', 'Edit the text']);
    });

    it('shows what the draft was written from, so it can be checked before anybody puts their name to it', () => {
        renderDraft(drafted());

        expect(screen.getByRole('button', { name: 'Citation 1: Contract addendum — signatures' })).toBeDefined();
    });

    it('opens the text for editing, and keeps what was typed', () => {
        renderDraft(drafted());

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));

        const written = screen.getByRole('textbox', { name: 'Draft text' });
        fireEvent.change(written, { target: { value: 'We accept, with a 5% cap.' } });

        expect(screen.getByRole('textbox', { name: 'Draft text' })).toHaveProperty(
            'value',
            'We accept, with a 5% cap.',
        );
    });

    it('says an edit goes nowhere, rather than leaving somebody to assume it was saved', () => {
        renderDraft(drafted());

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));

        expect(
            screen.getByText('Changes stay on this screen — nothing here saves or sends the draft.'),
        ).toBeDefined();
    });

    it('names a block it is not the renderer for rather than drawing somebody else’s data as a draft', () => {
        renderDraft({ type: null, named: 'RiskScore' });

        expect(screen.getByText('type: RiskScore')).toBeDefined();
    });
});
