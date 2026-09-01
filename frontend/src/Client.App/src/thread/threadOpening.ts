// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailThreadMessage, MailThreadPage } from '@mailfathom/client-backend';

// Which messages a conversation is open at when it is first drawn. A conversation of thirty messages that opened them
// all would be the same paragraph thirty times, and one that opened none would be a screen somebody has to scroll to
// the bottom of before it says anything — so it opens at the part they came for and collapses the rest.
//
// It is decided once, from what is held at the moment the conversation is first drawn, and never again: a later page
// arriving would otherwise close the message somebody is reading and open a newer one under their cursor. Every message
// a page adds after that is collapsed, and what is open is theirs from then on.

/** The conversation's messages so far, in its own order, across every page read. */
export function messagesOf(pages: readonly MailThreadPage[]): readonly MailThreadMessage[] {
    return pages.flatMap((page) => page.messages);
}

/** Whether the conversation as read so far holds the message named, which is what says a read may stop paging for it. */
export function holdsMessage(messages: readonly MailThreadMessage[], storedEmailId: string): boolean {
    return messages.some((message) => message.email.id === storedEmailId);
}

// How many messages a conversation opens itself at, where nobody named one. Each open message is a body this screen
// asks the deployment for, so the number is a request count rather than a matter of taste: a conversation of a hundred
// unread messages that opened all of them would put a hundred reads on the wire in one draw, which is the cost the
// mount-by-expansion design exists to refuse. Three is what catching up on a conversation usually means — the last
// thing said and the couple before it — and everything older is one line the reader opens if they want it.
const mostOpenedAtOnce = 3;

/**
 * Which messages a conversation opens expanded.
 *
 * The message somebody arrived at, where they arrived at one, because that is the context they came for. Else the most
 * recent of what they have not read, because catching up on a conversation is what opening one usually is. Else the
 * last of it, so a conversation everybody has read still opens on its most recent word rather than on its oldest.
 *
 * @param messages The conversation as read so far, in its own order.
 * @param openAt The message the conversation was opened at, or `null` where it was opened at none.
 * @returns The messages to draw expanded, by the identity each is reached by, at most {@link mostOpenedAtOnce} of them.
 */
export function openedBy(messages: readonly MailThreadMessage[], openAt: string | null): readonly string[] {
    if (openAt !== null && holdsMessage(messages, openAt)) {
        return [openAt];
    }

    const unread = messages.filter((message) => message.email.unread).map((message) => message.email.id);

    if (unread.length > 0) {
        return unread.slice(-mostOpenedAtOnce);
    }

    const last = messages[messages.length - 1];

    return last === undefined ? [] : [last.email.id];
}
