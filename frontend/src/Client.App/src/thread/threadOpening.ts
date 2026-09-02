// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailThreadMessage, MailThreadPage } from '@mailfathom/client-backend';

// Where a conversation puts the reader when it is first drawn. A conversation shows its latest message and hides
// everything before it behind one control, so the only question left is which message the reader arrived at: the one
// they named, where the conversation holds it, and the latest otherwise. That answer decides two things — where focus
// is placed, and whether the history starts shown, because arriving at a message the history holds cannot leave that
// message hidden.
//
// It is decided once, from what is held at the moment the conversation stops reading, and never again: a later page
// arriving would otherwise move the reader off the message they came for.

/** The conversation's messages so far, in its own order, across every page read. */
export function messagesOf(pages: readonly MailThreadPage[]): readonly MailThreadMessage[] {
    return pages.flatMap((page) => page.messages);
}

/** Whether the conversation as read so far holds the message named, which is what says a read may stop paging for it. */
export function holdsMessage(messages: readonly MailThreadMessage[], storedEmailId: string): boolean {
    return messages.some((message) => message.email.id === storedEmailId);
}

/**
 * The message a conversation arrives at.
 *
 * The message somebody was sent to, where they were sent to one the conversation holds, because that is the context
 * they came for. The latest of it otherwise, which is what a conversation opened on its own subject shows.
 *
 * @param messages The conversation as read so far, in its own order.
 * @param openAt The message the conversation was opened at, or `null` where it was opened at none.
 * @returns The message to arrive at, by the identity it is reached by, or `null` where there is no message to arrive at.
 */
export function arrivesAt(messages: readonly MailThreadMessage[], openAt: string | null): string | null {
    if (openAt !== null && holdsMessage(messages, openAt)) {
        return openAt;
    }

    return messages[messages.length - 1]?.email.id ?? null;
}
