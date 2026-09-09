// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailThreadMessage } from '@mailfathom/client-backend';

/** Which message a statement rests on, worded as the place it holds in the conversation and who wrote it. */
export interface ThreadStateSource {
    readonly storedEmailId: string;

    /** The message's place counted from one, which is what a reader counts and what the design project draws. */
    readonly position: number;

    /** Who wrote it, short enough to sit on one line beside the place. */
    readonly name: string;
}

/**
 * The message a statement's source names, among the ones this conversation holds.
 *
 * A source the conversation has not read yet resolves to nothing rather than to a place nobody can be taken to: the
 * link exists to reveal a message, and a link that reveals nothing is worse than no link at all.
 *
 * @param messages The conversation's messages, in its own order.
 * @param storedEmailId The message the statement rests on, or nothing where it names none.
 * @returns Where that message sits and who wrote it, or `null` where the conversation does not hold it.
 */
export function sourceOf(
    messages: readonly MailThreadMessage[],
    storedEmailId: string | undefined,
): ThreadStateSource | null {
    const held = messages.find((one) => one.email.id === storedEmailId);

    if (storedEmailId === undefined || held === undefined) {
        return null;
    }

    return { storedEmailId, position: held.position + 1, name: shortName(held) };
}

// The design project draws the source as a place and a first name, because the link sits on one line inside a card
// that is already narrow. A name is one word here and several there, so what is taken is the leading word rather than
// a name parsed into parts — and a message whose sender was never named falls back to the address, which is the only
// other thing a reader could recognize it by. A header naming the sender with no visible characters is never named
// either — the wire type admits it — and a link named by that would announce nothing at all.
function shortName(message: MailThreadMessage): string {
    const named = visible(message.email.senderDisplayName) ?? visible(message.email.senderAddress) ?? '';
    const leading = named.split(' ')[0] ?? '';

    return leading === '' ? named : leading;
}

function visible(text: string | null): string | null {
    const trimmed = text?.trim() ?? '';

    return trimmed === '' ? null : trimmed;
}
