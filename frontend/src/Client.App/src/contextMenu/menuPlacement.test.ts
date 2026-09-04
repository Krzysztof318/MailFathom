// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { menuEdge, placedWithin } from './menuPlacement';

const pane = { width: 400, height: 300 };
const menu = { width: 120, height: 200 };

describe('placedWithin', () => {
    it('opens the menu at the point the gesture happened where there is room for it', () => {
        expect(placedWithin({ x: 40, y: 30 }, menu, pane)).toStrictEqual({ x: 40, y: 30 });
    });

    it('pulls a menu opened near the end of the line back inside the pane', () => {
        expect(placedWithin({ x: 380, y: 30 }, menu, pane).x).toBe(pane.width - menu.width - menuEdge);
    });

    it('pulls a menu opened near the foot of the pane back up inside it', () => {
        expect(placedWithin({ x: 40, y: 290 }, menu, pane).y).toBe(pane.height - menu.height - menuEdge);
    });

    it('keeps a menu opened in the far corner clear of both edges at once', () => {
        expect(placedWithin({ x: 399, y: 299 }, menu, pane)).toStrictEqual({
            x: pane.width - menu.width - menuEdge,
            y: pane.height - menu.height - menuEdge,
        });
    });

    it('draws a menu taller than the pane from the near edge, so what is past the end of it can be scrolled to', () => {
        expect(placedWithin({ x: 40, y: 30 }, { width: 120, height: 500 }, pane).y).toBe(menuEdge);
    });

    it('keeps a menu opened at the very start of the pane off its edge', () => {
        expect(placedWithin({ x: 0, y: 0 }, menu, pane)).toStrictEqual({ x: menuEdge, y: menuEdge });
    });
});
