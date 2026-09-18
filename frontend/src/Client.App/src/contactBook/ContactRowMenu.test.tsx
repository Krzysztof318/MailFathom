// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Contact } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ContactRowMenu } from './ContactRowMenu';

const anna: Contact = {
    id: 'anna',
    displayName: 'Anna Kowalska',
    addresses: ['anna@contoso.example'],
    preferredAddress: 'anna@contoso.example',
    note: null,
    origin: 'Collected',
    recordedAt: '2026-08-31T09:41:00+00:00',
    amendedAt: '2026-08-31T09:41:00+00:00',
};

function composingWhere(offered: boolean, compose = vi.fn()): Composing {
    return { offered, drafts: false, opening: null, compose, close: vi.fn() };
}

function menuUnder({
    composing = composingWhere(true),
    erasable = true,
    onOpen = vi.fn(),
    onSelect = vi.fn(),
    onAskErasure = vi.fn(),
}: {
    composing?: Composing;
    erasable?: boolean;
    onOpen?: () => void;
    onSelect?: () => void;
    onAskErasure?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ComposingContext value={composing}>
                <ContactRowMenu
                    contact={anna}
                    at={{ x: 20, y: 30 }}
                    erasable={erasable}
                    onSelect={onSelect}
                    onOpen={onOpen}
                    onAskErasure={onAskErasure}
                    onClose={vi.fn()}
                />
            </ComposingContext>
        </LocalizationProvider>,
    );
}

function drawn(): (string | null)[] {
    return screen.getAllByRole('menuitem').map((item) => item.textContent);
}

describe('ContactRowMenu', () => {
    it('draws the acts this deployment publishes a route for, in the order the design draws them', () => {
        menuUnder();

        expect(drawn()).toStrictEqual(['Select contacts', 'Write a message', 'Open contact', 'Delete contact']);
    });

    it('leaves writing out where the credential may not write a message at all', () => {
        menuUnder({ composing: composingWhere(false) });

        expect(drawn()).toStrictEqual(['Select contacts', 'Open contact', 'Delete contact']);
    });

    it('leaves deleting out where the credential may not change the book', () => {
        menuUnder({ erasable: false });

        expect(drawn()).toStrictEqual(['Select contacts', 'Write a message', 'Open contact']);
    });

    it('opens a message addressed to the person the menu is about', () => {
        const compose = vi.fn();
        menuUnder({ composing: composingWhere(true, compose) });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Write a message' }));

        expect(compose).toHaveBeenCalledWith({ kind: 'new', to: ['anna@contoso.example'] });
    });

    it('raises the question an erasure stands behind rather than erasing on the press', () => {
        const onAskErasure = vi.fn();
        menuUnder({ onAskErasure });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete contact' }));

        expect(onAskErasure).toHaveBeenCalledTimes(1);
    });
});
