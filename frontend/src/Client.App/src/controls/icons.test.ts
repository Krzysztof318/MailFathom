// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { glyphIn, glyphOf, iconNames, nameOf } from './icons';

/**
 * Every number in an outline, which is what has to fall inside the box the same file declares.
 *
 * Path data writes a fraction below one without its leading zero — `q-.125 0-.25-.062` — so the leading digits are
 * optional. Reading `.625` as `625` is how this check reported a glyph as outside a box it sits well inside.
 */
function coordinatesIn(outline: string): readonly number[] {
    return [...outline.matchAll(/-?(?:\d+\.?\d*|\.\d+)/gu)].map(([number]) => Number(number));
}

describe('icons', () => {
    it('draws every symbol the client declares from a file committed in the tree', () => {
        const missing = iconNames.filter((name) => glyphOf(name) === undefined);

        expect(missing).toEqual([]);
    });

    it('carries no symbol the client does not draw, so nothing sits in the bundle unnoticed', () => {
        const committed = Object.keys(
            import.meta.glob('../assets/icons/*.svg', { query: '?raw', import: 'default', eager: true }),
        ).map(nameOf);

        expect([...committed].sort()).toEqual([...iconNames].sort());
    });

    // The failure this exists for renders rather than throwing: an outline drawn on the 24-unit box, put in the
    // 960-unit one, is a speck in the corner of an icon that is otherwise fine. Upstream exports both forms — most of
    // the `wght300_24px` files carry the 960 box and `auto_awesome` carries none at all — so the box travels with each
    // outline, and this is what says it kept travelling with the right one.
    it.each(iconNames)('draws %s inside the box its own file declares', (name) => {
        const glyph = glyphOf(name);
        const [left = 0, top = 0, width = 0, height = 0] = (glyph?.box ?? '').split(' ').map(Number);
        const drawn = coordinatesIn(glyph?.outline ?? '');

        // A relative segment carries an offset rather than a position, so the bound is the box's own extent rather
        // than its corners: a coordinate an order of magnitude outside it is the mismatch, not a rounded edge.
        expect(Math.max(...drawn)).toBeLessThanOrEqual(Math.max(Math.abs(left) + width, Math.abs(top) + height));
    });

    it('reads the box out of a file that declares one', () => {
        const glyph = glyphIn('<svg height="24" viewBox="0 -960 960 960" width="24"><path d="M0-960h960v960z"/></svg>');

        expect(glyph).toEqual({ box: '0 -960 960 960', outline: 'M0-960h960v960z' });
    });

    it('reads the box a file with no viewBox draws on, rather than assuming the one its siblings use', () => {
        const glyph = glyphIn(
            '<svg xmlns="http://www.w3.org/2000/svg" height="24" width="24"><path d="M1 2h3z"/></svg>',
        );

        expect(glyph).toEqual({ box: '0 0 24 24', outline: 'M1 2h3z' });
    });

    it('reads no symbol out of a file that carries no path', () => {
        expect(glyphIn('<svg height="24" width="24"><circle r="4" /></svg>')).toBeUndefined();
    });
});
