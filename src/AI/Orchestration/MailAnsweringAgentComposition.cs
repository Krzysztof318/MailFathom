// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Retrieval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Orchestration;

/// <summary>Composes the agent that answers a question about the mailbox.</summary>
/// <remarks>
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
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <returns>The composed agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ChatClientAgent Compose(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        ScopedMailKnowledgeRetrieval retrieval,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(retrieval);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var chatOptions = ChatGenerationParameterMapping.ToChatOptions(plan);

        // Carried as the run's instruction rather than written into a message, which is what keeps it in a position no
        // retrieved extract can be placed in.
        chatOptions.Instructions = MailAnsweringInstructions.Text;

        // The one tool, and the only route by which mail reaches the model. It is offered as a tool rather than through
        // the framework's text-search context provider because that provider publishes a query and nothing else, and
        // the narrowing a lookup needs is the greater part of what makes an answer reach the mail a search would.
        chatOptions.Tools = [retrieval.CreateSearchTool()];

        var options = new ChatClientAgentOptions
        {
            Name = AgentName,
            ChatOptions = chatOptions,
        };

        return new ChatClientAgent(chatClient, options, loggerFactory);
    }
}
