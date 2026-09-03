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

/** Where a conversation put the reader, and what was true of that place when it did. */
export interface Arrival {
    /** The message arrived at, by the identity it is reached by. */
    readonly storedEmailId: string;

    /**
     * Whether the conversation stood the reader in front of messages other than the one they arrived at.
     *
     * True exactly where the arrival is not the conversation's latest, which is the one case the pane opens with its
     * history shown. It decides that, and it decides whether the arrival is marked out from what surrounds it — both
     * being questions about where the reader landed rather than about what they have done since, which is why the
     * answer is taken here and never recomputed. A mark derived from what is drawn would appear on a message already
     * on the screen the moment somebody showed the history, moving its words sideways under them.
     */
    readonly amongOthers: boolean;
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

    return storedEmailId === null ? null : { storedEmailId, amongOthers: storedEmailId !== latest };
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
 * sent to and a mark saying otherwise would be a sentence that is not true. Nothing is marked either where the
 * conversation stood the reader in front of that message alone: a rule pointing at the only thing on the screen points
 * at nothing.
 *
 * Every answer here is a function of the arrival, which is decided once, so a mark neither appears nor disappears
 * while somebody reads. Showing the history is the gesture that would otherwise do it, and a message already on the
 * screen gaining a rule and an indent is words moving sideways under a reader.
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

    return arrival.amongOthers ? 'list' : null;
}
