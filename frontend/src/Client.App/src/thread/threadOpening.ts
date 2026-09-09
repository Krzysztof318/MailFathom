// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailThreadMessage, MailThreadPage } from '@mailfathom/client-backend';
import type { OpenConversation } from '../workspace/openConversation';

// Where a conversation puts the reader when it is first drawn. A conversation shows the message it was opened at and
// hides everything else behind one control, so the only question left is which message that is: the one they named,
// where the conversation holds it, and the latest otherwise. That answer decides two things — where focus is placed,
// and which message is marked out from the ones around it once the rest of the correspondence stands beside it.
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

/** Where a conversation put the reader, and what was true of that place when it did. */
export interface Arrival {
    /** The message arrived at, by the identity it is reached by. */
    readonly storedEmailId: string;
}

/**
 * Where a conversation arrives.
 *
 * The message somebody was sent to, where they were sent to one the conversation holds, because that is the context
 * they came for. The latest of it otherwise, which is what a conversation opened on its own subject shows.
 *
 * @param messages The conversation as read so far, in its own order.
 * @param openAt The message the conversation was opened at, or `null` where it was opened at none.
 * @returns Where to arrive, or `null` where there is no message to arrive at.
 */
export function arrivesAt(messages: readonly MailThreadMessage[], openAt: string | null): Arrival | null {
    const latest = messages[messages.length - 1]?.email.id ?? null;
    const storedEmailId = openAt !== null && holdsMessage(messages, openAt) ? openAt : latest;

    return storedEmailId === null ? null : { storedEmailId };
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
 * sent to and a mark saying otherwise would be a sentence that is not true.
 *
 * What is *not* asked here is whether the marked message is standing alone on the screen. A rule pointing at the only
 * thing drawn points at nothing, so that case carries no mark either — but it is a question about what is drawn rather
 * than about where the reader landed, and it changes under the one control the conversation offers. `Thread.tsx` asks
 * it where the messages are drawn, which is what keeps the mark on the message the list opened once the rest of the
 * correspondence is shown beside it, whether that message is the conversation's latest or one inside its history.
 *
 * @param conversation The conversation as it was opened.
 * @param arrival Where it arrived, or `null` where it has not decided yet.
 * @param settled Whether a landing has had its time and become an ordinary message.
 * @returns What marks the arrival, or `null`.
 */
export function arrivalMark(
    conversation: OpenConversation,
    arrival: Arrival | null,
    settled: boolean,
): ArrivalMark | null {
    if (arrival?.storedEmailId !== conversation.openAt) {
        return null;
    }

    if (conversation.fromResult === true) {
        return settled ? null : 'result';
    }

    return 'list';
}
