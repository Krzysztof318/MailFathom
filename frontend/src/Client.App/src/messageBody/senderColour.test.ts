// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readableRunColour } from './senderColour';

// The two panels the reduced view draws a message on, as `styles.css` declares them. A test asserting a hexadecimal
// answer would be asserting the walk's step size rather than what it is for, so every assertion here is a contrast
// reading against the surface the colour will actually be painted on.
const panels = { light: '#ffffff', dark: '#1d2128' };

function contrast(one: string, other: string): number {
    const luminance = (colour: string) => {
        const written = Number.parseInt(colour.slice(1), 16);
        const channel = (value: number) => {
            const scaled = value / 255;

            return scaled <= 0.04045 ? scaled / 12.92 : ((scaled + 0.055) / 1.055) ** 2.4;
        };

        return (
            0.2126 * channel((written >> 16) & 0xff) +
            0.7152 * channel((written >> 8) & 0xff) +
            0.0722 * channel(written & 0xff)
        );
    };

    const [lighter, darker] = [luminance(one), luminance(other)].sort((first, second) => second - first);

    return ((lighter ?? 0) + 0.05) / ((darker ?? 0) + 0.05);
}

function hue(colour: string): number {
    const written = Number.parseInt(colour.slice(1), 16);
    const red = ((written >> 16) & 0xff) / 255;
    const green = ((written >> 8) & 0xff) / 255;
    const blue = (written & 0xff) / 255;
    const spread = Math.max(red, green, blue) - Math.min(red, green, blue);

    if (spread === 0) {
        return 0;
    }

    return Math.max(red, green, blue) === red
        ? ((green - blue) / spread + (green < blue ? 6 : 0)) / 6
        : Math.max(red, green, blue) === green
          ? ((blue - red) / spread + 2) / 6
          : ((red - green) / spread + 4) / 6;
}

describe('readableRunColour', () => {
    it('leaves a colour alone where it already reads on the panel it will be drawn on', () => {
        // Near-black on white and near-white on the dark panel are each far past the bar, so neither moves.
        expect(readableRunColour('#111111')?.onLight).toBe('#111111');
        expect(readableRunColour('#eeeeee')?.onDark).toBe('#eeeeee');
    });

    it('lifts the ordinary dark signature colour until a reader can read it on the dark panel', () => {
        const readable = readableRunColour('#333333');

        expect(contrast(readable?.onDark ?? '', panels.dark)).toBeGreaterThanOrEqual(4.5);
    });

    it('takes a colour written for a dark composer down until it reads on the light panel', () => {
        const readable = readableRunColour('#dddddd');

        expect(contrast(readable?.onLight ?? '', panels.light)).toBeGreaterThanOrEqual(4.5);
    });

    it.each([['#333333'], ['#0048e0'], ['#008000'], ['#ff0000'], ['#800080'], ['#dddddd'], ['#808080']])(
        'answers %s as a pair a reader can read on both panels',
        (written) => {
            const readable = readableRunColour(written);

            expect(contrast(readable?.onLight ?? '', panels.light)).toBeGreaterThanOrEqual(4.5);
            expect(contrast(readable?.onDark ?? '', panels.dark)).toBeGreaterThanOrEqual(4.5);
        },
    );

    it('keeps the hue the sender chose, which is the whole of what they chose it for', () => {
        const readable = readableRunColour('#0048e0');

        // Within a degree of the blue that was written, on a circle a whole turn of which is one.
        expect(Math.abs(hue(readable?.onDark ?? '') - hue('#0048e0'))).toBeLessThan(1 / 360);
    });

    it.each([['red'], ['#fff'], ['rgb(0 0 0)'], [''], ['#12345g']])(
        'answers nothing for %s, which is not a colour the wire may carry',
        (written) => {
            expect(readableRunColour(written)).toBeNull();
        },
    );
});
