// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ContactRelationships;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.ContactRelationships;

/// <summary>Covers what the relationship agent is composed as: its name, its instruction, and what it may reach.</summary>
/// <remarks>
/// Run over a substituted chat client rather than described, so what is proved is the composition the framework actually
/// received.
/// </remarks>
public sealed class ContactRelationshipAgentCompositionTests
{
    private const string Answer =
        """{ "note": { "text": "They lead the addendum renegotiation.", "sources": [0] } }""";

    /// <summary>The correspondence is put to the agent in the turn, so a tool would be a way to reach mail beyond one contact.</summary>
    [Fact]
    public async Task Compose_TheRelationshipAgent_OffersNoToolAtAll()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "0. Conversation: the addendum",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(chatClient.Calls, call => Assert.True(call.Options?.Tools is null or []));
    }

    /// <summary>
    /// Over every language, so that composing the instruction for one and sending another is a failure here rather
    /// than something only the pure instruction tests would have noticed.
    /// </summary>
    [Theory]
    [InlineData(MailAccountLanguage.English)]
    [InlineData(MailAccountLanguage.Polish)]
    public async Task Compose_TheRelationshipAgent_CarriesItsOwnInstructionInsideTheEnvelope(
        MailAccountLanguage language)
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient, language);

        // Act
        await agent.RunAsync(
            "0. Conversation: the addendum",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            chatClient.Calls,
            call => Assert.Equal(ContactRelationshipInstructions.TextFor(language), call.Options?.Instructions));
    }

    /// <summary>The name is what the composition is recorded under, so it is this agent's alone.</summary>
    [Fact]
    public void Compose_TheRelationshipAgent_IsNamedApartFromEveryOtherAgent()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("{}");

        // Act
        var agent = AgentOver(chatClient);

        // Assert
        Assert.Equal(ContactRelationshipAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(MailAnsweringAgentComposition.AgentName, agent.Name);
    }

    private static ChatClientAgent AgentOver(
        ScriptedChatClient chatClient,
        MailAccountLanguage language = MailAccountLanguage.English) =>
        ContactRelationshipAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            language,
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
}
