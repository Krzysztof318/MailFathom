// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { DiscardConfirmation } from './DiscardConfirmation';

function drawConfirmation(written: boolean): {
    discarded: ReturnType<typeof vi.fn>;
    kept: ReturnType<typeof vi.fn>;
} {
    const discarded = vi.fn();
    const kept = vi.fn();

    render(
        <LocalizationProvider>
            <DiscardConfirmation written={written} onDiscard={discarded} onKeep={kept} />
        </LocalizationProvider>,
    );

    return { discarded, kept };
}

function close(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));
}

describe('DiscardConfirmation', () => {
    it('closes without asking where there is nothing to lose', () => {
        const { discarded } = drawConfirmation(false);

        close();

        expect(discarded).toHaveBeenCalledTimes(1);
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('asks before throwing away what somebody wrote, and names what goes', () => {
        const { discarded } = drawConfirmation(true);

        close();

        expect(discarded).not.toHaveBeenCalled();

        const asked = screen.getByRole('dialog').textContent;

        expect(asked).toContain('Discard this message?');
        expect(asked).toContain('along with the draft your deployment is holding for it');
    });

    it('throws it away once that is confirmed', () => {
        const { discarded, kept } = drawConfirmation(true);

        close();
        fireEvent.click(screen.getByRole('button', { name: 'Discard' }));

        expect(discarded).toHaveBeenCalledTimes(1);
        expect(kept).not.toHaveBeenCalled();
    });

    it('files it as a draft instead where that is what was asked for', () => {
        const { discarded, kept } = drawConfirmation(true);

        close();
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        expect(kept).toHaveBeenCalledTimes(1);
        expect(discarded).not.toHaveBeenCalled();
    });

    it('does neither on the way back to writing', () => {
        const { discarded, kept } = drawConfirmation(true);

        close();
        fireEvent.click(screen.getByRole('button', { name: 'Back to writing' }));

        expect(discarded).not.toHaveBeenCalled();
        expect(kept).not.toHaveBeenCalled();
    });
});
