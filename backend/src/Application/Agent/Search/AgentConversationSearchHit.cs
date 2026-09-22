// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;

namespace MailFathom.Application.Agent.Search;

/// <summary>One conversation a ranking placed, and the message in it that placed it there.</summary>
/// <param name="Conversation">The conversation found.</param>
/// <param name="Title">What the conversation is called, or <see langword="null" /> where it was never named.</param>
/// <param name="LastActivityAt">When anything was last written into it, which is what settles a tie between two scores.</param>
/// <param name="Message">The message that matched: a question or instruction the person wrote, or the answer a block belongs to.</param>
/// <param name="Sequence">The place of the entry that matched, which is where a screen opens the conversation.</param>
/// <remarks>
/// A ranking reads a conversation once, at its best-matching entry, so a conversation that says the same thing twenty
/// times is still one result. The title travels with the hit rather than being read afterwards, because the history a
/// person searches is theirs alone and a ranking already joins the row it comes from.
/// </remarks>
public sealed record AgentConversationSearchHit(
    AgentConversationId Conversation,
    string? Title,
    DateTimeOffset LastActivityAt,
    AgentMessageId Message,
    long Sequence);
