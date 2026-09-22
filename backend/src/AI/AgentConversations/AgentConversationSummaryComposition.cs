// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.AgentConversations;

/// <summary>Composes the agent that compacts an Agent conversation into a summary.</summary>
/// <remarks>
/// Its tool set is empty, and that is the capability: summarising is a reading of text the conversation already holds,
/// so an agent that could reach mail would be one a pasted message could talk into fetching some.
/// </remarks>
internal static class AgentConversationSummaryComposition
{
    /// <summary>The name this agent is composed under.</summary>
    internal const string AgentName = "mailfathom-conversation-compaction";

    /// <summary>Composes the summarising agent over a chat client.</summary>
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
        var operation = new AgentOperation(AgentName, AgentConversationSummaryInstructions.Text, []);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
