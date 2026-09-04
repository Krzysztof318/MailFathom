// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The ramp a mailbox mark is drawn from, which `styles.css` declares as `--color-mailbox-mark-1` and the numbers after
// it. Only the rule for reading it lives here; the hues themselves are the token layer's, as every colour in this
// client is.
//
// It sits beside `MailboxMark.tsx` rather than inside it for the reason `initials.ts` sits beside `PersonAvatar.tsx`:
// the rule is a value a test can state an expectation about, and a module a component is named after would collide
// with the component's own file on a filesystem that ignores case.

/**
 * How many hues `styles.css` declares the ramp with. It is the one thing outside that file that moves when the ramp
 * grows: declare `--color-mailbox-mark-4` there and raise this to four.
 */
const declaredHues = 3;

/**
 * Which hue of the ramp the mailbox at this ordinal takes, counted from one as the tokens are. Ordinal zero is the row
 * standing for every mailbox at once and is not one of them — `MailboxMark` draws that as a split of the first two.
 *
 * A deployment reads as many mailboxes as it was configured with, which is more than any ramp declares hues for, so
 * something has to be decided for the ones past the last. The ramp starts again at its first: two distant mailboxes
 * sharing a hue costs far less than one mailbox carrying no mark at all, and the mark never stands alone anyway — the
 * name beside it is what says which mailbox this is. It is written down here rather than left to whatever a modulo
 * happens to do, so that the answer is a decision a reader can find rather than one they have to derive.
 */
export function mailboxMarkHue(ordinal: number): number {
    return ((ordinal - 1) % declaredHues) + 1;
}
