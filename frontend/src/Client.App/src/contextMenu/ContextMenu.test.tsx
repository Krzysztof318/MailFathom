// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ContextMenu, type ContextMenuItem } from './ContextMenu';

const archiving = vi.fn();

const items: readonly ContextMenuItem[] = [
    { icon: 'check_box', label: 'Select messages', choose: vi.fn() },
    { icon: 'archive', label: 'Archive', choose: archiving },
    { icon: 'delete', label: 'Delete', destroys: true, choose: vi.fn() },
];

function menuOver(closed: () => void = vi.fn()): void {
    render(
        <LocalizationProvider>
            <ContextMenu header="Contract annex — signatures" at={{ x: 40, y: 60 }} items={items} onClose={closed} />
        </LocalizationProvider>,
    );
}

function walked(): HTMLElement[] {
    return screen.getAllByRole('menuitem');
}

describe('ContextMenu', () => {
    it('announces as a menu named by what it is about', () => {
        menuOver();

        expect(screen.getByRole('menu', { name: 'Contract annex — signatures' })).toBeTruthy();
    });

    it('draws every item it was given, in the order it was given them', () => {
        menuOver();

        expect(walked().map((item) => item.textContent)).toStrictEqual(['Select messages', 'Archive', 'Delete']);
    });

    it('puts focus on the first item as it opens, so the keyboard is already in the menu', () => {
        menuOver();

        expect(document.activeElement).toBe(walked()[0]);
    });

    it('walks down the items with the arrow keys', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });

        expect(document.activeElement).toBe(walked()[1]);
    });

    it('walks back up them the same way', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowUp' });

        expect(document.activeElement).toBe(walked()[0]);
    });

    it('comes round to the last item from the first, so nothing is unreachable in one direction', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowUp' });

        expect(document.activeElement).toBe(walked()[2]);
    });

    it('reaches the ends of the menu directly', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'End' });

        expect(document.activeElement).toBe(walked()[2]);
    });

    it('closes on Escape', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

        expect(closed).toHaveBeenCalledOnce();
    });

    it('closes rather than letting the keyboard tab out behind it', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Tab' });

        expect(closed).toHaveBeenCalledOnce();
    });

    it('closes on a press outside it', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.pointerDown(document.body);

        expect(closed).toHaveBeenCalledOnce();
    });

    it('stays open when the press was inside it, which is a reader reaching for an item', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.pointerDown(screen.getByRole('menuitem', { name: 'Archive' }));

        expect(closed).not.toHaveBeenCalled();
    });

    it('performs the item that was chosen and goes', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }));

        expect(archiving).toHaveBeenCalledOnce();
        expect(closed).toHaveBeenCalledOnce();
    });
});
