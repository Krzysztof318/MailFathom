// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// A sender picks their colours against the page their mail composer drew, which is white, and says nothing about the
// one the reduced view actually draws them on. So a dark theme turns a perfectly ordinary `#333333` signature into text
// nobody can read, and the reader has no control that would fix it — which is an accessibility failure rather than a
// preference, and it is answered by lifting the colour rather than by dropping it: the sender meant something by it.
//
// Both answers are computed here, because no component asks which theme is in force — `theme/Theme.tsx` states that
// rule — so the run carries the colour it takes on each surface and the stylesheet picks between them.

/** The one notation `Client.Backend` lets a run's colour through as. */
const sixDigitHex = /^#[0-9a-f]{6}$/iu;

/** WCAG 1.4.3's bar for ordinary body text, which is what a run is. */
const requiredContrast = 4.5;

/**
 * What the reduced view draws a message on under each theme: `--color-panel` in `styles.css`, resolved to sRGB. They
 * are written here rather than read from the document because this is a pure function a test can hold to a number, and
 * because a run is rendered before any layout has measured anything.
 */
const panels = { light: 0xffffff, dark: 0x1d2128 };

export interface ReadableRunColour {
    /** What the sender's colour becomes on the light theme's panel. */
    readonly onLight: string;

    /** The same colour on the dark theme's panel, lifted until a reader can actually read it. */
    readonly onDark: string;
}

/**
 * Answers the sender's colour as the two a reader can read, or `null` where the value is not a colour this client
 * understands — in which case the run is drawn in the theme's own text colour, which is readable by construction.
 */
export function readableRunColour(foreground: string): ReadableRunColour | null {
    if (!sixDigitHex.test(foreground)) {
        return null;
    }

    const written = Number.parseInt(foreground.slice(1), 16);

    return {
        onLight: liftAgainst(written, panels.light),
        onDark: liftAgainst(written, panels.dark),
    };
}

/**
 * Moves the colour away from the surface, keeping its hue and its saturation, until it clears the contrast bar or runs
 * out of room. Only lightness moves: a colour pulled toward grey would answer the contrast question while losing the
 * one thing the sender chose it for.
 */
function liftAgainst(colour: number, surface: number): string {
    if (contrast(colour, surface) >= requiredContrast) {
        return hexOf(colour);
    }

    // Away from the surface, so a dark panel lifts the run toward white and a light one takes it toward black.
    const toward = luminance(surface) > 0.5 ? 0 : 1;
    const [hue, saturation, lightness] = toHsl(colour);

    // Sixty-four steps over the room that is left, which is finer than any colour a display can tell apart and cheap
    // enough to run per run: the walk stops at the first one that clears the bar, so the colour stays as close to what
    // the sender wrote as the surface allows.
    for (let step = 1; step <= 64; step += 1) {
        const moved = fromHsl(hue, saturation, lightness + ((toward - lightness) * step) / 64);

        if (contrast(moved, surface) >= requiredContrast) {
            return hexOf(moved);
        }
    }

    // Nothing on that line clears it, which happens for a saturated hue against a mid surface. The far end is what is
    // left, and it is still the most readable version of the colour the sender wrote.
    return hexOf(fromHsl(hue, saturation, toward));
}

function contrast(one: number, other: number): number {
    const [lighter, darker] = [luminance(one), luminance(other)].sort((first, second) => second - first);

    return ((lighter ?? 0) + 0.05) / ((darker ?? 0) + 0.05);
}

function luminance(colour: number): number {
    const channel = (value: number) => {
        const scaled = value / 255;

        return scaled <= 0.04045 ? scaled / 12.92 : ((scaled + 0.055) / 1.055) ** 2.4;
    };

    return (
        0.2126 * channel((colour >> 16) & 0xff) +
        0.7152 * channel((colour >> 8) & 0xff) +
        0.0722 * channel(colour & 0xff)
    );
}

function toHsl(colour: number): [number, number, number] {
    const red = ((colour >> 16) & 0xff) / 255;
    const green = ((colour >> 8) & 0xff) / 255;
    const blue = (colour & 0xff) / 255;

    const highest = Math.max(red, green, blue);
    const lowest = Math.min(red, green, blue);
    const spread = highest - lowest;
    const lightness = (highest + lowest) / 2;

    if (spread === 0) {
        return [0, 0, lightness];
    }

    const saturation = spread / (1 - Math.abs(2 * lightness - 1));
    const hue =
        highest === red
            ? ((green - blue) / spread + (green < blue ? 6 : 0)) / 6
            : highest === green
              ? ((blue - red) / spread + 2) / 6
              : ((red - green) / spread + 4) / 6;

    return [hue, saturation, lightness];
}

function fromHsl(hue: number, saturation: number, lightness: number): number {
    const held = Math.min(1, Math.max(0, lightness));
    const reach = saturation * Math.min(held, 1 - held);

    // CSS Color 4's own conversion, with the hue in turns rather than in degrees: each channel is the same wave read a
    // third of the circle apart, which is what keeps the hue exactly where the sender put it as the lightness moves.
    const at = (channel: number) => {
        const position = (channel + hue * 12) % 12;

        return Math.round((held - reach * Math.max(-1, Math.min(position - 3, 9 - position, 1))) * 255);
    };

    return (at(0) << 16) | (at(8) << 8) | at(4);
}

function hexOf(colour: number): string {
    return `#${colour.toString(16).padStart(6, '0')}`;
}
