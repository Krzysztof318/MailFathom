// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.Agent.Conversations;

/// <summary>Builds the entries a conversation is written as, stamped with the places a store would have given them.</summary>
/// <remarks>
/// The places are stamped here because the store derives them where it writes the row, so an entry composed in process
/// carries none. A test that wants to read a conversation back therefore has to say what order it was written in, and
/// saying it once here keeps each case about what it is asserting rather than about counting.
/// </remarks>
internal static class AgentConversationExample
{
    /// <summary>The conversation every example belongs to.</summary>
    internal static AgentConversationId Conversation { get; } = AgentConversationId.New();

    /// <summary>Stamps entries with consecutive places, exactly as writing them one after another would.</summary>
    /// <param name="entries">The entries, in the order they were written.</param>
    /// <returns>The same entries, each naming the conversation and the place it holds.</returns>
    internal static IReadOnlyList<AgentConversationEntry> Written(params AgentConversationEntry[] entries) =>
        [.. entries.Select((entry, index) => entry with { ConversationId = Conversation, Sequence = index + 1 })];

    /// <summary>A question somebody asked about their whole mailbox.</summary>
    /// <param name="message">The message the question is.</param>
    /// <param name="text">What they asked.</param>
    /// <returns>The entry.</returns>
    internal static AgentMessageWritten Question(AgentMessageId message, string text) =>
        new(message, AgentMessageAuthor.Person, PresentationText.Create(text), AgentMessageScope.Mailbox());

    /// <summary>A line of the agent's own.</summary>
    /// <param name="message">The message the line is.</param>
    /// <param name="text">What it says.</param>
    /// <returns>The entry.</returns>
    internal static AgentMessageWritten Note(AgentMessageId message, string text) =>
        new(message, AgentMessageAuthor.Agent, PresentationText.Create(text), Scope: null);

    /// <summary>A block the reader only reads, which is the first of the catalogue's.</summary>
    /// <returns>The block.</returns>
    internal static PresentationBlock Reading() =>
        PresentationPlanExample.EveryBlock().First(block => !block.Type.Actionable);

    /// <summary>A block the reader acts on, which is what an offer is presented as.</summary>
    /// <returns>The block.</returns>
    internal static PresentationBlock Actionable() =>
        PresentationPlanExample.EveryBlock().First(block => block.Type.Actionable);

    /// <summary>What accepting an offer would carry out: a message to one reserved address.</summary>
    /// <returns>The act.</returns>
    internal static AgentProposedAct Act()
    {
        if (!EmailAddress.TryCreate("Ada", "ada@northwind.example", out var recipient))
        {
            throw new InvalidOperationException("The example recipient is a valid address.");
        }

        return new AgentMessageSending(
            "work",
            [recipient],
            PresentationText.Create("The quote"),
            PresentationText.Create("Thank you, we accept."));
    }
}
