// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Search;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.Search;

/// <summary>Covers what the phrase-reading agent is composed as: its name, its instruction, and what it may reach.</summary>
/// <remarks>
/// Run over a substituted chat client rather than described, so what is proved is the composition the framework
/// actually received.
/// </remarks>
public sealed class MailSearchPhraseAgentCompositionTests
{
    /// <summary>Reading a sentence is a reading of the sentence, so an agent a sentence could talk into fetching mail is one this composition never builds.</summary>
    [Fact]
    public async Task Compose_ThePhraseReadingAgent_OffersNoToolAtAll()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("""{"criteria": ["invoice"]}""");
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Sentence: unread mail about the invoice",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(chatClient.Calls, call => Assert.True(call.Options?.Tools is null or []));
    }

    [Fact]
    public async Task Compose_ThePhraseReadingAgent_CarriesItsOwnInstructionInsideTheEnvelope()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("""{"criteria": ["invoice"]}""");
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Sentence: unread mail about the invoice",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            chatClient.Calls,
            call => Assert.Equal(MailSearchPhraseInstructions.Text, call.Options?.Instructions));
    }

    /// <summary>A name of its own, so what a deployment spends on reading sentences is separable from what it spends answering questions.</summary>
    [Fact]
    public void Compose_ThePhraseReadingAgent_IsNamedApartFromEveryOtherAgent()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("{}");

        // Act
        var agent = AgentOver(chatClient);

        // Assert
        Assert.Equal(MailSearchPhraseAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(DiscoveryPlanningAgentComposition.AgentName, agent.Name);
    }

    private static ChatClientAgent AgentOver(ScriptedChatClient chatClient) =>
        MailSearchPhraseAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
}
