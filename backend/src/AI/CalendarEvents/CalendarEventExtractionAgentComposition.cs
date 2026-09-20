// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.CalendarEvents;

/// <summary>Composes the agent that reads text into the calendar events it names.</summary>
/// <remarks>
/// It is composed the way every agent in this product is: one instruction carried as the agent's own, one declared
/// tool set, one name, and the registered instruction envelope wrapped around it. The tool set is empty, and that is
/// the capability rather than a description of one — the text is put to the agent in the turn, so an agent that could
/// look anything up would be an agent able to reach mail beyond the text it was asked about, and one that could write
/// would be an agent able to put an appointment on somebody's calendar.
/// </remarks>
internal static class CalendarEventExtractionAgentComposition
{
    /// <summary>The name this agent is composed under, for both the message it reads and the sentence it reads.</summary>
    internal const string AgentName = "mailfathom-calendar-event-extraction";

    /// <summary>Composes the extraction agent over a chat client.</summary>
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
        var operation = new AgentOperation(AgentName, CalendarEventExtractionInstructions.Text, []);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
