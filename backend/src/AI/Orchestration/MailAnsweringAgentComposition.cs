// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Retrieval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Orchestration;

/// <summary>Declares the agent that answers a question about the mailbox, and composes it like every other operation.</summary>
/// <remarks>
/// <para>
/// What is here is what answering decides for itself: its name, its instruction, and its one tool.
/// <see cref="AgentComposition" /> is what turns those into an agent, and everything true of every operation — where
/// the instruction is carried, the <see cref="IAgentInstructionEnvelope" /> wrapped around it, the parameters each turn
/// runs with — is stated there rather than again here. The word <em>envelope</em> below keeps the meaning this file
/// already gave it, which is the element the retrieval formats an extract into.
/// </para>
/// <para>
/// The composition is separate from what opens the provider connection, so what the agent is can be exercised over a
/// substituted chat client and no network: the tool loop, the scope the retrieval is bound to, and the parameters the
/// deployment declared are all decided here.
/// </para>
/// <para>
/// The agent holds one capability and it reads. There is no tool that sends, deletes, moves, or marks mail, and that is a
/// property of this expression rather than a rule stated somewhere else — a mutating agent would be one composed with a
/// mutating tool, and this composes none.
/// </para>
/// <para>
/// Retrieved mail and the instructions the system writes reach the model in two different positions, and this is where
/// that is decided. The instruction is the agent's, carried on every turn; an extract is the result of a tool call,
/// written into the envelope the retrieval formats. Neither is ever composed into the other, so no message can arrive
/// where an instruction is read.
/// </para>
/// <para>
/// The scope is not among the tool's arguments and cannot be, because the tool is built by the retrieval the scope was
/// bound into. That is visible here as an absence, which is why it is stated: a composition that passed the scope
/// through the same options the model reads would have made the boundary a rule rather than a shape.
/// </para>
/// </remarks>
internal static class MailAnsweringAgentComposition
{
    /// <summary>Names the composed agent in whatever reads a run.</summary>
    internal const string AgentName = "mailfathom-mail-answering";

    /// <summary>Composes the agent over one chat client and one run's retrieval.</summary>
    /// <param name="chatClient">The provider-neutral client every turn of the run is sent through.</param>
    /// <param name="plan">The validated declaration: the generation parameters one call runs with.</param>
    /// <param name="retrieval">The mail this run may reach, already bound to the caller's scope.</param>
    /// <param name="instructionEnvelope">Supplies the text placed before and after this operation's instruction.</param>
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <returns>The composed agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ChatClientAgent Compose(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        ScopedMailKnowledgeRetrieval retrieval,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(retrieval);

        // The one tool, and the only route by which mail reaches the model. It is offered as a tool rather than through
        // the framework's text-search context provider because that provider publishes a query and nothing else, and
        // the narrowing a lookup needs is the greater part of what makes an answer reach the mail a search would.
        var operation = new AgentOperation(
            AgentName,
            MailAnsweringInstructions.Text,
            [retrieval.CreateSearchTool()]);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
