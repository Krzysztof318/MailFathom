// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Carries out an act a person accepted, exactly as it was proposed.</summary>
/// <remarks>
/// A seam for the acceptance's own suite rather than for a second implementation: whether an acceptance is recorded,
/// refused, or ended as failed is decided apart from the drafting and sending use cases the act goes through, which
/// their own suites cover.
/// </remarks>
public interface IAgentActPerformer
{
    /// <summary>Carries out one accepted act.</summary>
    /// <param name="act">The act, exactly as it was proposed.</param>
    /// <param name="proposal">Names the proposal, which keys the act so a retried acceptance is recognizably the same request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true" /> when the act was carried out; <see langword="false" /> when this deployment refused it.</returns>
    Task<bool> PerformAsync(AgentProposedAct act, string proposal, CancellationToken cancellationToken);
}
