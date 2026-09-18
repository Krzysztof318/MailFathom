// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Contact } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ContactSelectionBar } from './ContactSelectionBar';

function personCalled(id: string, displayName: string): Contact {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        origin: 'Asserted',
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
    };
}

const picked = [personCalled('anna', 'Anna Kowalska'), personCalled('bartek', 'Bartek Nowak')];

function composingWhere(offered: boolean, compose = vi.fn()): Composing {
    return { offered, drafts: false, opening: null, compose, close: vi.fn() };
}

function drawBar({
    selected = picked,
    erasable = true,
    composing = composingWhere(true),
    onClear = vi.fn(),
    onAskErasure = vi.fn(),
}: {
    selected?: readonly Contact[];
    erasable?: boolean;
    composing?: Composing;
    onClear?: () => void;
    onAskErasure?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ComposingContext value={composing}>
                <ContactSelectionBar
                    selected={selected}
                    erasable={erasable}
                    onClear={onClear}
                    onAskErasure={onAskErasure}
                />
            </ComposingContext>
        </LocalizationProvider>,
    );
}

describe('ContactSelectionBar', () => {
    it('says how many people are picked out, where the toolbar would otherwise stand', () => {
        drawBar();

        expect(screen.getByRole('toolbar', { name: 'Selected contacts' })).toBeDefined();
        expect(screen.getByText('2 selected')).toBeDefined();
    });

    it('opens one message addressed to everybody picked out rather than one message each', () => {
        const compose = vi.fn();
        drawBar({ composing: composingWhere(true, compose) });

        fireEvent.click(screen.getByRole('button', { name: 'Write' }));

        expect(compose).toHaveBeenCalledWith({
            kind: 'new',
            to: ['anna@contoso.example', 'bartek@contoso.example'],
        });
    });

    it('leaves the selection rather than acting on it when the way out is pressed', () => {
        const onClear = vi.fn();
        const onAskErasure = vi.fn();
        drawBar({ onClear, onAskErasure });

        fireEvent.click(screen.getByRole('button', { name: 'Clear the selection' }));

        expect(onClear).toHaveBeenCalledTimes(1);
        expect(onAskErasure).not.toHaveBeenCalled();
    });

    it('offers no deletion where the credential may not change the book', () => {
        drawBar({ erasable: false });

        expect(screen.queryByRole('button', { name: 'Delete contacts' })).toBeNull();
    });
});
