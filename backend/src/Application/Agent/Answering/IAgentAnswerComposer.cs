// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Composes the Agent's answer to one question, writing each part into the journal as it is ready.</summary>
/// <remarks>
/// <para>
/// The seam between the use case and whatever composes an agent. What crosses it is the brief and the journal; no model,
/// no tool, and no provider type does. An implementation reads what the person's grant lets it read, and answers every
/// act with a proposal written through <see cref="AgentAnswerJournal.ProposeAsync" /> rather than by performing it.
/// </para>
/// <para>
/// It returns once it has said everything and never ends the answer itself: the ending is the use case's, which is what
/// lets a failure, a stop, and a completion all be recorded by one owner. What it returns is what a completed ending
/// carries — the questions the agent suggests asking next — so they are recorded only for an answer that completed.
/// </para>
/// </remarks>
public interface IAgentAnswerComposer
{
    /// <summary>Composes the answer.</summary>
    /// <param name="brief">The question, the language, and the conversation before it.</param>
    /// <param name="journal">Where every part of the answer is written the moment it exists.</param>
    /// <param name="cancellationToken">Cancels the run: a stop, the run's own ceiling on time, or the process stopping.</param>
    /// <returns>The questions the agent suggests asking next, each one an <see cref="AgentFollowUps.IsAskable" /> line, and empty where it suggested none.</returns>
    Task<IReadOnlyList<PresentationText>> ComposeAsync(AgentAnswerBrief brief, AgentAnswerJournal journal, CancellationToken cancellationToken);
}
