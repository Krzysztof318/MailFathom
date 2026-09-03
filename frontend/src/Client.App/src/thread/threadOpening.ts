// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailThreadMessage, MailThreadPage } from '@mailfathom/client-backend';
import type { OpenConversation } from '../workspace/openConversation';

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

/**
 * How the message a conversation arrived at is marked out from the ones around it.
 *
 * `list` is the durable one: somebody opened this message and the conversation around it is the context, so it keeps
 * the accent rule and says so in its head for as long as the conversation is open. `result` is the transient one: it
 * says the client took somebody where they asked to go, and it settles into an ordinary message once it has been seen.
 */
export type ArrivalMark = 'list' | 'result';

/**
 * What marks the message a conversation arrived at, or `null` where nothing does.
 *
 * Nothing is marked where the conversation was opened on its own subject, because there is no message somebody was
 * sent to and a mark saying otherwise would be a sentence that is not true. Nothing is marked either where one message
 * is all that is drawn: a rule pointing at the only thing on the screen points at nothing, which is why the count is
 * of what is drawn rather than of what the conversation holds — a conversation with its history folded away draws one
 * message however many it has read.
 *
 * @param conversation The conversation as it was opened.
 * @param arrival The message it arrived at, or `null` where it has not decided yet.
 * @param drawn The messages actually on the screen.
 * @param settled Whether a landing has had its time and become an ordinary message.
 * @returns What marks the arrival, or `null`.
 */
export function arrivalMark(
    conversation: OpenConversation,
    arrival: string | null,
    drawn: readonly MailThreadMessage[],
    settled: boolean,
): ArrivalMark | null {
    if (arrival === null || arrival !== conversation.openAt) {
        return null;
    }

    if (conversation.fromResult === true) {
        return settled ? null : 'result';
    }

    return drawn.length > 1 ? 'list' : null;
}
