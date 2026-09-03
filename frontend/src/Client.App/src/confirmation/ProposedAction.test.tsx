// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ProposedAction, type Proposal } from './ProposedAction';

const filing: Proposal = {
    action: 'Archive the 12 newsletters from last week',
    reason: 'None of them was opened and none is part of a thread you answered.',
    impact: 'They leave Inbox on the work account and are found in Archive.',
    reversal: { kind: 'undoable', forSeconds: 10 },
    confirmationRequired: true,
};

function drawProposal(proposal: Proposal = filing): {
    agreed: ReturnType<typeof vi.fn>;
    dismissed: ReturnType<typeof vi.fn>;
} {
    const agreed = vi.fn();
    const dismissed = vi.fn();

    render(
        <LocalizationProvider>
            <ProposedAction proposal={proposal} agreeing="Archive them" onAgreed={agreed} onDismissed={dismissed} />
        </LocalizationProvider>,
    );

    return { agreed, dismissed };
}

// What the card itself shows, rather than the whole document: the confirmation it holds repeats the action and the
// impact even while closed — `textContent` reads a `display: none` subtree exactly as it reads any other — so a
// document-wide assertion would pass for a card that drew neither of them.
function offered(): HTMLElement {
    return screen.getByRole('region', { name: 'MailFathom suggests this. Nothing has happened yet.' });
}

describe('ProposedAction', () => {
    it('performs nothing on being drawn, which is what a suggestion is', () => {
        const { agreed } = drawProposal();

        expect(agreed).not.toHaveBeenCalled();
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('shows what it would do, why it was offered, and what would change', () => {
        drawProposal();

        const shown = offered().textContent;

        expect(shown).toContain('Archive the 12 newsletters from last week');
        expect(shown).toContain('None of them was opened');
        expect(shown).toContain('They leave Inbox on the work account');
    });

    it('says that a confirmation stands between agreeing and anything changing', () => {
        drawProposal();

        expect(offered().textContent).toContain('You are asked to confirm this before anything changes.');
    });

    it('says an act that reaches nothing outside the client happens as soon as it is agreed to', () => {
        drawProposal({ ...filing, confirmationRequired: false });

        expect(offered().textContent).toContain('it happens as soon as you agree');
    });

    it('asks the confirmation rather than acting, where the act needs one', () => {
        const { agreed } = drawProposal();

        fireEvent.click(screen.getByRole('button', { name: 'Archive them' }));

        expect(agreed).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog').textContent).toContain('Archive the 12 newsletters from last week');
    });

    it('performs the act once the confirmation is answered', () => {
        const { agreed } = drawProposal();

        fireEvent.click(screen.getByRole('button', { name: 'Archive them' }));
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Archive them' }));

        expect(agreed).toHaveBeenCalledTimes(1);
    });

    it('performs the act without a confirmation where none is required', () => {
        const { agreed } = drawProposal({ ...filing, confirmationRequired: false });

        fireEvent.click(screen.getByRole('button', { name: 'Archive them' }));

        expect(agreed).toHaveBeenCalledTimes(1);
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('leaves everything alone where the offer is turned down', () => {
        const { agreed, dismissed } = drawProposal();

        fireEvent.click(screen.getByRole('button', { name: 'Not now' }));

        expect(dismissed).toHaveBeenCalledTimes(1);
        expect(agreed).not.toHaveBeenCalled();
    });
});
