// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Control } from './Control';

describe('Control', () => {
    it('carries its name as words where the shape draws them', () => {
        render(<Control label="New message" icon="edit_square" onPress={() => undefined} />);

        expect(screen.getByRole('button', { name: 'New message' }).textContent).toBe('New message');
    });

    it('carries its name for the accessibility tree where the shape draws the symbol alone', () => {
        render(<Control label="Reply" icon="reply" shape="symbol" onPress={() => undefined} />);

        const control = screen.getByRole('button', { name: 'Reply' });

        expect(control.textContent).toBe('');
        expect(control.getAttribute('aria-label')).toBe('Reply');
    });

    it.each([
        ['on', true, 'true'],
        ['off', false, 'false'],
    ])('says a state it holds is %s, so what it is set to is read rather than only seen', (_name, held, said) => {
        render(
            <Control
                label="Hide the panels"
                icon="fullscreen"
                shape="symbol"
                pressed={held}
                onPress={() => undefined}
            />,
        );

        expect(screen.getByRole('button', { name: 'Hide the panels' }).getAttribute('aria-pressed')).toBe(said);
    });

    // The other half of that, and the one a wrong default would break everywhere at once: a control that does a thing
    // rather than holding a state reports none, because a button carrying `aria-pressed` is announced as a toggle.
    it('reports no state at all for a control that holds none', () => {
        render(<Control label="New message" icon="edit_square" onPress={() => undefined} />);

        expect(screen.getByRole('button', { name: 'New message' }).hasAttribute('aria-pressed')).toBe(false);
    });

    it('does what it is for when it is pressed, which is what makes it not a planned one', () => {
        const pressed = vi.fn();

        render(<Control label="New message" onPress={pressed} />);

        fireEvent.click(screen.getByRole('button', { name: 'New message' }));

        expect(pressed).toHaveBeenCalledTimes(1);
    });
});
