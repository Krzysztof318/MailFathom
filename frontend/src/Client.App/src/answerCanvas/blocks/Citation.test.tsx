// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { DeclaredSource } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { Citation } from './Citation';

const agreement: DeclaredSource = {
    id: 'c-1',
    kind: 'email',
    label: 'Master agreement.pdf',
    medium: 'Written',
    unreadable: null,
};

function renderCitation({
    declared = true,
    follow = null,
}: { declared?: boolean; follow?: ((of: string) => void) | null } = {}) {
    const sources = new Map(declared ? [[agreement.id, agreement]] : []);

    return render(
        <LocalizationProvider>
            <AnswerSourcesContext value={{ sources, follow }}>
                <Citation position={1} source="c-1" />
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

describe('Citation', () => {
    it('is a control carrying the name of the source rather than a bare number', () => {
        renderCitation({ follow: vi.fn() });

        expect(screen.getByRole('button', { name: 'Citation 1: Master agreement.pdf' }).textContent).toBe('1');
    });

    it('follows the source it names', () => {
        const follow = vi.fn();
        renderCitation({ follow });

        fireEvent.click(screen.getByRole('button'));

        expect(follow).toHaveBeenCalledWith('c-1');
    });

    it('stays reachable and says why it cannot act where there is nowhere to follow it to', () => {
        renderCitation();

        const chip = screen.getByRole('button');

        expect(chip.getAttribute('aria-disabled')).toBe('true');
        expect(chip.getAttribute('aria-label')).toContain('not built yet');
    });

    it('says a source this client was never given rather than drawing the fact uncited', () => {
        renderCitation({ declared: false, follow: vi.fn() });

        // The reason is its own sentence rather than one wrapped in another: every other citation on the screen
        // follows, so reporting this one as a capability nobody has built would be false as well as doubled.
        expect(screen.getByRole('button').getAttribute('aria-label')).toBe(
            'Citation 1 — this client was not given the source behind it',
        );
    });
});
