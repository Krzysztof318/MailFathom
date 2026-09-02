// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ListWidthGrip } from './ListWidthGrip';
import { listWidthStep, narrowestList, startingListWidth, widestList } from './listWidth';

function renderGrip(width = 400): { chosen: ReturnType<typeof vi.fn>; grip: HTMLElement } {
    const chosen = vi.fn();

    render(
        <LocalizationProvider>
            <ListWidthGrip width={width} onWidth={vi.fn()} onChosen={chosen} />
        </LocalizationProvider>,
    );

    return { chosen, grip: screen.getByRole('separator') };
}

describe('ListWidthGrip', () => {
    it('stands as a separator naming what it moves, so a reader finds it by what it is', () => {
        const { grip } = renderGrip();

        expect(screen.getByRole('separator', { name: 'Message list width' })).toBe(grip);
        expect(grip.getAttribute('aria-orientation')).toBe('vertical');
    });

    it('reports where the boundary stands and how far it may go', () => {
        const { grip } = renderGrip(400);

        expect(grip.getAttribute('aria-valuenow')).toBe('400');
        expect(grip.getAttribute('aria-valuemin')).toBe(String(narrowestList));
        expect(grip.getAttribute('aria-valuemax')).toBe(String(widestList));
    });

    it('is reachable from the keyboard, which is the whole of what makes the split operable without a mouse', () => {
        const { grip } = renderGrip();

        grip.focus();

        expect(document.activeElement).toBe(grip);
    });

    it('narrows the list by one step on the left arrow', () => {
        const { chosen, grip } = renderGrip(400);

        fireEvent.keyDown(grip, { key: 'ArrowLeft' });

        expect(chosen).toHaveBeenCalledWith(400 - listWidthStep);
    });

    it('widens the list by one step on the right arrow', () => {
        const { chosen, grip } = renderGrip(400);

        fireEvent.keyDown(grip, { key: 'ArrowRight' });

        expect(chosen).toHaveBeenCalledWith(400 + listWidthStep);
    });

    it('returns the split to the width it started at on Home', () => {
        const { chosen, grip } = renderGrip(500);

        fireEvent.keyDown(grip, { key: 'Home' });

        expect(chosen).toHaveBeenCalledWith(startingListWidth);
    });

    it('returns the split to the width it started at on a double-click, which is the same act with a mouse', () => {
        const { chosen, grip } = renderGrip(500);

        fireEvent.doubleClick(grip);

        expect(chosen).toHaveBeenCalledWith(startingListWidth);
    });

    it('moves the boundary with the pointer, and reports the width it was let go at', () => {
        const moved = vi.fn();
        const chosen = vi.fn();

        render(
            <LocalizationProvider>
                <ListWidthGrip width={400} onWidth={moved} onChosen={chosen} />
            </LocalizationProvider>,
        );

        const grip = screen.getByRole('separator');
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 600 });
        fireEvent.pointerMove(grip, { pointerId: 1, clientX: 660 });
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 680 });

        expect(moved).toHaveBeenCalledWith(460);
        expect(chosen).toHaveBeenCalledWith(480);
    });

    it('ignores a second pointer arriving mid-drag, so two fingers cannot fight over one boundary', () => {
        const moved = vi.fn();

        render(
            <LocalizationProvider>
                <ListWidthGrip width={400} onWidth={moved} onChosen={vi.fn()} />
            </LocalizationProvider>,
        );

        const grip = screen.getByRole('separator');
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 600 });
        fireEvent.pointerMove(grip, { pointerId: 2, clientX: 900 });

        expect(moved).not.toHaveBeenCalled();
    });

    it('moves nothing on a pointer that never took the boundary', () => {
        const moved = vi.fn();

        render(
            <LocalizationProvider>
                <ListWidthGrip width={400} onWidth={moved} onChosen={vi.fn()} />
            </LocalizationProvider>,
        );

        fireEvent.pointerMove(screen.getByRole('separator'), { pointerId: 1, clientX: 900 });

        expect(moved).not.toHaveBeenCalled();
    });

    it('leaves a key it does not answer to whatever is behind it', () => {
        const { chosen, grip } = renderGrip();

        fireEvent.keyDown(grip, { key: 'PageDown' });

        expect(chosen).not.toHaveBeenCalled();
    });
});
