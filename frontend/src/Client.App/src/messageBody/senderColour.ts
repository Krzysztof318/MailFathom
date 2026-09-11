// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What the reduced view does with a colour the sender wrote, which is to normalise it rather than to reproduce it.
//
// **The reading surfaces are for reading, so they are drawn in this client's own colours.** A sender picks theirs
// against the white page their composer drew, with no idea of the panel a reader's theme paints, and they pick them to
// decorate a newsletter rather than to tell a reader anything — so a pane that honoured them would be a pane whose
// every message is a different colour, each of them legible by luck. Formatting is what carries the sender's meaning
// here: bold, italic, underline, monospace and a strikethrough are kept exactly as they arrived, because each of them
// says something a colour does not.
//
// **One distinction survives, because losing it makes a message harder to read rather than more uniform.** A sender who
// greyed a disclaimer, a postal address, or the small print under a signature was saying *this is not the message*, and
// a pane that drew it in the body colour would put the small print in competition with the words it sits under. So a
// colour that recedes from the body text is drawn in the client's own receding token, and every other colour is drawn
// in the body text colour — two answers, the same two for every message in the mailbox.
//
// Both are tokens rather than values, which is what makes the dark theme free: the stylesheet already states each of
// them for both panels, so nothing here computes a contrast and nothing asks which theme is in force.
//
// The sender's own markup is the surface that still shows their colours, and that is what that surface is for. This
// governs the reduced document and the cleaned rendering drawn from it, those being one tree.

/** The one notation `Client.Backend` lets a run's colour through as. */
const sixDigitHex = /^#[0-9a-f]{6}$/iu;

/**
 * How far from grey a colour may be and still read as one the sender chose to recede rather than to decorate with.
 *
 * A saturated colour is a decoration whatever its lightness — a pale pink heading is not small print — so it takes the
 * body colour like every other decoration.
 */
const greyestSaturation = 0.25;

/**
 * How light a near-grey has to be before it reads as receding rather than as the message's own text.
 *
 * Senders write body text anywhere from `#000000` to about `#333333`, which is a third of the way up, and write their
 * small print from about `#666666` upward. The line falls between the two.
 */
const recedingLightness = 0.35;

/** What the pane draws a run in, given the colour the sender wrote. */
export type SenderColourRole =
    /** The sender's colour stepped back from their own body text, so this client's does too. */
    | 'receding'

    /** Everything else, which is the body text colour: a decoration the reading surface does not reproduce. */
    | 'ordinary';

/**
 * Answers which of this client's own two colours a run the sender coloured is drawn in.
 *
 * @param foreground The colour as the document carries it, which is `#rrggbb` or nothing this client understands.
 * @returns The role, with a colour in any other notation answered as `ordinary` — an unreadable value decides nothing,
 * and the body text colour is readable by construction.
 */
export function senderColourRole(foreground: string): SenderColourRole {
    if (!sixDigitHex.test(foreground)) {
        return 'ordinary';
    }

    const [saturation, lightness] = greyness(Number.parseInt(foreground.slice(1), 16));

    return saturation <= greyestSaturation && lightness >= recedingLightness ? 'receding' : 'ordinary';
}

/** How far from grey the colour is and how light it is, which is the HSL pair the two bounds above are read against. */
function greyness(colour: number): [number, number] {
    const red = ((colour >> 16) & 0xff) / 255;
    const green = ((colour >> 8) & 0xff) / 255;
    const blue = (colour & 0xff) / 255;

    const highest = Math.max(red, green, blue);
    const lowest = Math.min(red, green, blue);
    const spread = highest - lowest;
    const lightness = (highest + lowest) / 2;

    return spread === 0 ? [0, lightness] : [spread / (1 - Math.abs(2 * lightness - 1)), lightness];
}
