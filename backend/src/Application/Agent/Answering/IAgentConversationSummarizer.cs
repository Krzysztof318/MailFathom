// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Answering;

/// <summary>Summarises the earlier part of an Agent conversation, so a turn can send the summary in its place.</summary>
/// <remarks>
/// <para>
/// The seam between the use case and the agent that summarises. What crosses it is text the conversation already holds
/// — the summary before this one and the turns written since — and what comes back is text; no model, no provider type,
/// and no tool does. The summary is written for the model that reads the next turn and never for a person, who is never
/// shown one.
/// </para>
/// <para>
/// A summary folds in the one before it, so nothing the conversation said falls out of what the next turn is composed
/// from however many times it has been compacted.
/// </para>
/// </remarks>
public interface IAgentConversationSummarizer
{
    /// <summary>Summarises the turns since the last summary, together with that summary.</summary>
    /// <param name="previousSummary">The newest summary the conversation already holds, or <see langword="null" /> where it was never compacted.</param>
    /// <param name="turns">The turns written since that summary which the new one stands in for, oldest first.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The summary, or <see langword="null" /> where no model could produce one — which the caller answers by composing from as much recent history as fits rather than by failing the turn.</returns>
    Task<string?> SummarizeAsync(
        string? previousSummary,
        IReadOnlyList<AgentHistoryTurn> turns,
        CancellationToken cancellationToken);
}
