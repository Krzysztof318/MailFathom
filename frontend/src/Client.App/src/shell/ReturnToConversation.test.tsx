// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ReturnToConversation } from './ReturnToConversation';

describe('ReturnToConversation', () => {
    it('goes back to the conversation that was left, named by what it says on it', () => {
        const onReturn = vi.fn();
        render(
            <LocalizationProvider>
                <ReturnToConversation onReturn={onReturn} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: 'Back to the conversation' }));

        expect(onReturn).toHaveBeenCalledOnce();
    });
});
