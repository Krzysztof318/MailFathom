// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Answering;

/// <summary>A question the conversation has already recorded, and the answer it opened.</summary>
/// <param name="Conversation">The conversation it was asked in.</param>
/// <param name="User">Whose conversation it is.</param>
/// <param name="Text">What was asked.</param>
/// <param name="Scope">What it was asked about, or <see langword="null" /> for a question narrowing nothing.</param>
/// <param name="Answer">The answer the question opened, which is the run composing it.</param>
/// <param name="OpenedAt">The place the answer was opened at.</param>
/// <param name="AskedAt">The instant the question was asked, which the run's time anchor states.</param>
public sealed record AgentQuestion(
    AgentConversationId Conversation,
    UserId User,
    PresentationText Text,
    AgentMessageScope? Scope,
    AgentMessageId Answer,
    long OpenedAt,
    DateTimeOffset AskedAt);
