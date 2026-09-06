// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Discovery;

/// <summary>Composes the agent that turns one question and its retrieved mail into a result.</summary>
/// <remarks>
/// It is composed the way every agent in this product is: one instruction carried as the agent's own, one declared tool
/// set, one name, and the registered instruction envelope wrapped around it. The tool set is empty, as the planning
/// agent's is, and for a stricter reason: this agent is shown mail, and a tool would be a way for that mail to talk it
/// into reading more.
/// </remarks>
internal static class DiscoveryCompositionAgentComposition
{
    /// <summary>The name this agent is composed under.</summary>
    internal const string AgentName = "mailfathom-discovery-composition";

    /// <summary>Composes the composing agent over a chat client.</summary>
    /// <param name="chatClient">The client the one call is made through.</param>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the agent logs through.</param>
    /// <returns>The composed agent.</returns>
    internal static ChatClientAgent Compose(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory)
    {
        var operation = new AgentOperation(AgentName, DiscoveryCompositionInstructions.Text, []);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
