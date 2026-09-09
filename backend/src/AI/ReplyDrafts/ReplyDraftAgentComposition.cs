// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Composes the agent that writes a reply out of the conversation it answers.</summary>
/// <remarks>
/// It is composed the way every agent in this product is: one instruction carried as the agent's own, one declared tool
/// set, one name, and the registered instruction envelope wrapped around it. The tool set is empty, and that is the
/// capability rather than a description of one — the conversation is put to the agent in the turn, so an agent that
/// could look anything up would be an agent able to reach mail beyond the exchange it was asked to answer, and one
/// that could act would be a draft that sends itself.
/// </remarks>
internal static class ReplyDraftAgentComposition
{
    /// <summary>The name this agent is composed under.</summary>
    internal const string AgentName = "mailfathom-reply-draft";

    /// <summary>Composes the reply-drafting agent over a chat client.</summary>
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
        var operation = new AgentOperation(AgentName, ReplyDraftInstructions.Text, []);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
