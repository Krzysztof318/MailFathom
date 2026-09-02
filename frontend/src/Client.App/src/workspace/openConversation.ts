// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The conversation somebody has open, which stands in front of the message they opened it from rather than replacing
// it: the workspace still holds that message, so closing the conversation returns to it and nothing had to remember
// where it came from.
//
// It carries the message it was opened at because arriving at a specific message inside a conversation is how anything
// but the mail list reaches one — a search result and an evidence citation both name a message rather than a thread —
// and a conversation opened at its beginning would have discarded the context somebody came for.

/** The conversation being read, and the message it was opened at. */
export interface OpenConversation {
    /** The conversation, as a message row published it. */
    readonly threadId: string;

    /** The message to open at, expanded and given focus, or `null` to open at what the conversation itself decides. */
    readonly openAt: string | null;
}

/**
 * The conversation's identity as one string.
 *
 * What a conversation opens with is decided once, from the messages held at the moment it is first drawn, so opening
 * the same conversation at another message is a screen of its own rather than the same one adjusted. This is what says
 * so to React.
 */
export function conversationKey(conversation: OpenConversation): string {
    return `${conversation.threadId}:${conversation.openAt ?? ''}`;
}
