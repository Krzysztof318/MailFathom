// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, type RenderResult } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import type { Contact } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { PersonPage } from './PersonPage';
import type { ContactCorrespondenceInForce } from './useContactCorrespondence';

function personCalled(origin: Contact['origin'], id = 'anna', displayName = 'Anna Kowalska'): Contact {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        origin,
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
    };
}

const nothingCorrelated: ContactCorrespondenceInForce = {
    correspondence: null,
    reading: false,
    failure: null,
    readAgain: () => undefined,
};

function composingWhere(offered: boolean, compose = vi.fn()): Composing {
    return { offered, drafts: false, opening: null, compose, close: vi.fn() };
}

function pageOf({
    contact = personCalled('Asserted'),
    writable = true,
    promoting = false,
    promotionSaid = null,
    onBack = null,
    composing = composingWhere(true),
    onPromote = vi.fn(),
    onAskErasure = vi.fn(),
}: {
    contact?: Contact;
    writable?: boolean;
    promoting?: boolean;
    promotionSaid?: 'people.written' | null;
    onBack?: (() => void) | null;
    composing?: Composing;
    onPromote?: () => void;
    onAskErasure?: () => void;
} = {}): ReactNode {
    return (
        <LocalizationProvider>
            <ComposingContext value={composing}>
                <PersonPage
                    contact={contact}
                    correspondence={nothingCorrelated}
                    writable={writable}
                    promoting={promoting}
                    promotionSaid={promotionSaid}
                    onBack={onBack}
                    onPromote={onPromote}
                    onAskErasure={onAskErasure}
                    onOpenThread={vi.fn()}
                    onOpenDocument={vi.fn()}
                />
            </ComposingContext>
        </LocalizationProvider>
    );
}

function drawPage(page: Parameters<typeof pageOf>[0] = {}): RenderResult {
    return render(pageOf(page));
}

describe('PersonPage', () => {
    it('draws who the person is and how to reach them', () => {
        drawPage();

        expect(screen.getByRole('heading', { name: 'Anna Kowalska' })).toBeDefined();
        expect(screen.getByText('anna@contoso.example')).toBeDefined();
    });

    // A record a mailbox collected is nobody's to amend, and the page says so on the record rather than leaving
    // somebody to discover it from a refusal.
    // Opening somebody is a view change, and this page is not remounted between two of them — so nothing would move
    // focus by itself, and at the width where the book behind is made inert there is not even a stale element left
    // holding it. Both moves are asserted: the one that opens the first person, and the one that swaps a second in.
    it('takes focus into the person opened, and again when a second person is opened in the same pane', () => {
        const { rerender } = drawPage();

        expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Anna Kowalska' }));

        rerender(pageOf({ contact: personCalled('Asserted', 'bartek', 'Bartek Nowak') }));

        expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Bartek Nowak' }));
    });

    // The other half of it: a render that is not a new person leaves focus wherever the reader put it, or every
    // answer arriving about the person already open would pull focus back out of whatever they had reached.
    it('leaves focus alone where the person has not changed', () => {
        const { rerender } = drawPage();

        screen.getByRole('button', { name: 'Delete contact' }).focus();
        rerender(pageOf({ promoting: true }));

        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Delete contact' }));
    });

    it('says a collected record is not the reader’s to amend, and offers taking it on', () => {
        drawPage({ contact: personCalled('Collected') });

        expect(screen.getByText('Read-only — editable once added to your contacts')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Add to my contacts' })).toBeDefined();
    });

    it('offers taking on nothing for a record the reader already asserted', () => {
        drawPage();

        expect(screen.queryByRole('button', { name: 'Add to my contacts' })).toBeNull();
    });

    it('says a promotion is in flight rather than leaving the control silent', () => {
        drawPage({ contact: personCalled('Collected'), promoting: true });

        expect(screen.getByRole('button', { name: 'Adding…' })).toBeDefined();
    });

    it('offers neither act on the record where the credential may not change the book', () => {
        drawPage({ contact: personCalled('Collected'), writable: false });

        expect(screen.queryByRole('button', { name: 'Add to my contacts' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Delete contact' })).toBeNull();
    });

    it('raises the question an erasure stands behind rather than erasing on the press', () => {
        const onAskErasure = vi.fn();
        drawPage({ onAskErasure });

        fireEvent.click(screen.getByRole('button', { name: 'Delete contact' }));

        expect(onAskErasure).toHaveBeenCalledTimes(1);
    });

    it('opens a message addressed to the person whose page this is', () => {
        const compose = vi.fn();
        drawPage({ composing: composingWhere(true, compose) });

        fireEvent.click(screen.getByRole('button', { name: 'Write' }));

        expect(compose).toHaveBeenCalledWith({ kind: 'new', to: ['anna@contoso.example'] });
    });

    // The way back belongs to the page rather than to the frame, and only where the list and the person cannot stand
    // together.
    it('draws the way back only where the list is behind this page rather than beside it', () => {
        drawPage();

        expect(screen.queryByRole('button', { name: 'Back to the address book' })).toBeNull();

        const onBack = vi.fn();
        drawPage({ onBack });

        fireEvent.click(screen.getByRole('button', { name: 'Back to the address book' }));

        expect(onBack).toHaveBeenCalledTimes(1);
    });

    it('says what the last write to this record did, beside the record it did it to', () => {
        drawPage({ promotionSaid: 'people.written' });

        expect(screen.getByRole('status').textContent).toBe('Contact added.');
    });
});
