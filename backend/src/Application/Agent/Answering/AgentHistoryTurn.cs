// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;

namespace MailFathom.Application.Agent.Answering;

/// <summary>One earlier turn of the conversation, as the model is shown it.</summary>
/// <param name="Author">Who said it.</param>
/// <param name="Text">What was said: a question, the agent's own line, or the text of an answer it composed.</param>
/// <remarks>
/// A turn is text rather than the entries it was written as, because what an earlier answer means to the next question
/// is what it said, not how it was laid out. A proposal is reduced to a line saying what was offered and where it stands,
/// so a model is never shown an act in a form it could mistake for one it may repeat without asking.
/// </remarks>
public sealed record AgentHistoryTurn(AgentMessageAuthor Author, string Text);
