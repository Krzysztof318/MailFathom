// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>One line of a person's conversation history: what a conversation is called and when it last moved.</summary>
/// <param name="Id">What addresses the conversation.</param>
/// <param name="Title">What it is called, and <see langword="null" /> while nothing has named it.</param>
/// <param name="StartedAt">When it was started, in UTC.</param>
/// <param name="LastActivityAt">When anything was last written into it, in UTC, which is what the history is ordered by.</param>
/// <remarks>
/// It carries no part of the conversation — no question, no block, no source — because a history list draws a name and
/// a time, and nothing that reads a list has any use for what the conversations said. The title is still the person's
/// own material, being composed from their question, so this is sensitive exactly as the rest of the record is.
/// </remarks>
public sealed record AgentConversationSummary(
    AgentConversationId Id,
    string? Title,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt);
