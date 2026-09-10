// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ChoiceSegment } from './ChoiceSegment';

// What the two segments say on the screen this stands in for. Held here rather than written into the markup below,
// where it would read as copy this repository keeps in a catalogue rather than as a fixture.
const light = 'Light';
const dark = 'Dark';

/** A group of two segments as every screen draws one: several of them sharing a name. */
function drawn(chosen: string, onChoose: (value: string) => void = () => undefined) {
    return render(
        <>
            <ChoiceSegment shape="row" name="theme" value="light" chosen={chosen === 'light'} onChoose={onChoose}>
                {light}
            </ChoiceSegment>
            <ChoiceSegment shape="row" name="theme" value="dark" chosen={chosen === 'dark'} onChoose={onChoose}>
                {dark}
            </ChoiceSegment>
        </>,
    );
}

describe('ChoiceSegment', () => {
    it('is one choice out of a group, reported under the words it carries', () => {
        drawn('light');

        expect(screen.getByRole('radio', { name: light, checked: true })).toBeDefined();
        expect(screen.getByRole('radio', { name: dark, checked: false })).toBeDefined();
    });

    it('answers with the value of the segment that was chosen, which the caller reads back into its own set', () => {
        const chosen = vi.fn();

        drawn('light', chosen);
        fireEvent.click(screen.getByRole('radio', { name: dark }));

        expect(chosen).toHaveBeenCalledWith('dark');
    });

    // The radio is hidden by being taken out of the flow, so whichever ancestor is positioned is the one it stands
    // against. A panel that scrolls and is not one reports it as overflow standing below everything a reader can see,
    // which is a second scrollbar on a surface that already has one — so the segment carries the containing block.
    it('keeps the radio it hides inside a box of its own, rather than against whatever happens to be positioned', () => {
        drawn('light');

        expect(screen.getByRole('radio', { name: light }).parentElement?.classList.contains('relative')).toBe(true);
    });
});
