// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.BodyCleanup;

/// <summary>Composes the agent that decides which blocks of one body a reader is shown.</summary>
/// <remarks>
/// Composed the way every agent in this product is: one instruction carried as the agent's own, one declared tool set, one
/// name, and the registered instruction envelope wrapped around it. The tool set is empty, and that is the capability
/// rather than a description of one — the outline is put to the agent in the turn, so an agent that could look anything up
/// would be an agent able to reach mail beyond the message it was asked about.
/// </remarks>
internal static class MailBodyCleanupAgentComposition
{
    /// <summary>The name this agent is composed under.</summary>
    internal const string AgentName = "mailfathom-body-cleanup";

    /// <summary>Composes the body-cleanup agent over a chat client.</summary>
    /// <param name="chatClient">The client the one call is made through.</param>
    /// <param name="plan">The generation parameters this pass runs on, which may name a model of its own.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the agent logs through.</param>
    /// <returns>The composed agent.</returns>
    internal static ChatClientAgent Compose(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory)
    {
        var operation = new AgentOperation(AgentName, MailBodyCleanupInstructions.Text, []);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
