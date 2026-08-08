// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
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
/// The agent is given no instructions. What separates retrieved mail from the instructions the system writes belongs to
/// the context formatter rather than to the composition, so the framework's own context prompt stands until that formatter
/// exists.
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

        var options = new ChatClientAgentOptions
        {
            Name = AgentName,
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = plan.MaximumOutputTokens,
                Temperature = plan.Temperature,
                TopP = plan.TopP,
            },

            // The one context provider, and the only route by which mail reaches the model.
            AIContextProviders = [retrieval.CreateContextProvider(loggerFactory)],
        };

        return new ChatClientAgent(chatClient, options, loggerFactory);
    }
}
