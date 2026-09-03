// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Switch } from './Switch';

// What the label around the switch says on the screen this stands in for. Held here rather than written into the
// markup below, where it would read as copy this repository keeps in a catalogue rather than as a fixture.
const named = 'Tab mode';

/** The switch as both screens draw it: inside the label that names what it decides. */
function drawn(on: boolean, onChange: (on: boolean) => void, disabled = false) {
    return render(
        <label>
            {named}
            <Switch on={on} disabled={disabled} onChange={onChange} />
        </label>,
    );
}

describe('Switch', () => {
    it('is reported as on or off under the name of the label around it', () => {
        drawn(true, () => undefined);

        expect(screen.getByRole('switch', { name: named, checked: true })).toBeDefined();
    });

    it('reports being turned on', () => {
        const chosen = vi.fn();

        drawn(false, chosen);
        fireEvent.click(screen.getByRole('switch'));

        expect(chosen).toHaveBeenCalledWith(true);
    });

    it('reports being turned off', () => {
        const chosen = vi.fn();

        drawn(true, chosen);
        fireEvent.click(screen.getByRole('switch'));

        expect(chosen).toHaveBeenCalledWith(false);
    });

    // What an inert switch refuses is the browser's own activation behaviour, which `fireEvent` dispatches past, so
    // what is asserted here is the state a browser reads that from rather than a click it would never have delivered.
    it('is inert rather than absent where the screen cannot act on it', () => {
        drawn(false, () => undefined, true);

        expect(screen.getByRole('switch').hasAttribute('disabled')).toBe(true);
    });
});
