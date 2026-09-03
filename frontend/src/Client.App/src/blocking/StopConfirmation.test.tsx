// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { StopConfirmation } from './StopConfirmation';

const leavesBehind = 'What has already moved stays on the new server, and the rest stays where it is.';

function drawing(onStop: () => void): void {
    render(
        <LocalizationProvider>
            <StopConfirmation leavesBehind={leavesBehind} onStop={onStop} />
        </LocalizationProvider>,
    );
}

function press(name: string): void {
    fireEvent.click(screen.getByRole('button', { name }));
}

describe('StopConfirmation', () => {
    it('shows nothing until the control is pressed, so the question is what a press opens', () => {
        drawing(vi.fn());

        expect(screen.queryByRole('heading', { name: 'Are you sure you want to stop?' })).toBeNull();

        press('Cancel');

        expect(screen.getByRole('heading', { name: 'Are you sure you want to stop?' })).toBeDefined();
    });

    it('names what stopping would leave behind, in the words the operation supplied', () => {
        drawing(vi.fn());

        press('Cancel');

        expect(screen.getByText(leavesBehind)).toBeDefined();
    });

    it('leaves the operation running when the answer is to continue it', () => {
        const stopped = vi.fn();

        drawing(stopped);
        press('Cancel');
        press('Continue the operation');

        expect(stopped).not.toHaveBeenCalled();
        expect(screen.queryByRole('heading', { name: 'Are you sure you want to stop?' })).toBeNull();
    });

    it('stops the operation once, and only once the answer is to stop it', () => {
        const stopped = vi.fn();

        drawing(stopped);
        press('Cancel');
        press('Yes, stop');

        expect(stopped).toHaveBeenCalledOnce();
    });

    it('asks again the next time, because neither answer is remembered', () => {
        drawing(vi.fn());
        press('Cancel');
        press('Continue the operation');
        press('Cancel');

        expect(screen.getByRole('heading', { name: 'Are you sure you want to stop?' })).toBeDefined();
    });
});
