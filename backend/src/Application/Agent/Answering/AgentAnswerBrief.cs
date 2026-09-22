// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Everything the composition is handed to answer one question.</summary>
/// <param name="Question">The question and the answer it opened.</param>
/// <param name="Language">The language the agent's own words are written in, which is the person's.</param>
/// <param name="History">The conversation before this question, oldest first and already bounded.</param>
/// <remarks>
/// The language is the person's rather than any mailbox's, because the agent's own words are made on the asking, for
/// one person, and read by nobody else. Quoted mail and a derivation already made about a mailbox keep their own
/// languages; that is the composition's rule to state, and nothing here decides it.
/// </remarks>
public sealed record AgentAnswerBrief(
    AgentQuestion Question,
    UserLanguage Language,
    IReadOnlyList<AgentHistoryTurn> History);
