// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.AgentConversations;

/// <summary>Composes the Agent and the turn it is asked on, the one way a deployment and an evaluation both reach it.</summary>
internal static class AgentConversationComposition
{
    /// <summary>Names the composed agent in whatever reads a run.</summary>
    internal const string AgentName = "mailfathom-agent";

    /// <summary>Composes the Agent over a client and the tools one run is offered.</summary>
    /// <param name="chatClient">The client every call is sent through, already carrying the run's ceiling and its steering.</param>
    /// <param name="plan">The validated declaration: which endpoint answers and with which parameters.</param>
    /// <param name="language">The language the person reads the Agent's own words in.</param>
    /// <param name="tools">The tools the person's grant allows.</param>
    /// <param name="instructionEnvelope">Supplies the text every composed agent's instruction is wrapped in.</param>
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <returns>The composed agent.</returns>
    internal static ChatClientAgent Compose(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        UserLanguage language,
        IReadOnlyList<AITool> tools,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory) =>
        AgentComposition.Compose(
            chatClient,
            plan,
            new AgentOperation(AgentName, AgentConversationInstructions.TextFor(language), tools),
            instructionEnvelope,
            loggerFactory);

    /// <summary>Composes the question's turn: the instant it was asked at, the person's accounts, what they are looking at, and the question.</summary>
    /// <param name="askedAt">When the question was asked, which is what a relative date in it is read against.</param>
    /// <param name="accounts">The accounts the person reads, by the deployment's own names for them.</param>
    /// <param name="scope">What the question was asked about, where it named something.</param>
    /// <param name="question">The question, already guarded for egress.</param>
    /// <returns>The turn's text.</returns>
    /// <remarks>
    /// The anchor and the accounts ride on the turn rather than in the instruction, for the reason every agent here puts
    /// the anchor there: the instruction is this build's own text, the same for every run, and these change per run.
    /// The accounts are the deployment's own names for them rather than addresses.
    /// </remarks>
    internal static string ComposeTurn(
        DateTimeOffset askedAt,
        IEnumerable<MailAccountId> accounts,
        AgentMessageScope? scope,
        string question)
    {
        var looking = scope is { Kind: AgentScopeKind.Thread, Subject: { } thread }
            ? string.Create(CultureInfo.InvariantCulture, $"The person is looking at the conversation with id {thread}.\n")
            : string.Empty;
        var named = string.Join(", ", accounts.Select(static account => account.Value));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AgentTimeAnchor.Stated(askedAt)}\nThe person's accounts: {named}.\n{looking}\n{question}");
    }
}
