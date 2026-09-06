// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what the composing agent is composed as: its name, its instruction, and what it may reach.</summary>
/// <remarks>
/// Run over a substituted chat client rather than described, so what is proved is the composition the framework
/// actually received.
/// </remarks>
public sealed class DiscoveryCompositionAgentCompositionTests
{
    /// <summary>This agent is shown mail, so a tool would be a way for that mail to talk it into reading more.</summary>
    [Fact]
    public async Task Compose_TheComposingAgent_OffersNoToolAtAll()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("""{"answer": "They accepted.", "sources": []}""");
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Question: which supplier quoted least",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(chatClient.Calls, call => Assert.True(call.Options?.Tools is null or []));
    }

    [Fact]
    public async Task Compose_TheComposingAgent_CarriesItsOwnInstructionInsideTheEnvelope()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("""{"answer": "They accepted.", "sources": []}""");
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Question: which supplier quoted least",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            chatClient.Calls,
            call => Assert.Equal(DiscoveryCompositionInstructions.Text, call.Options?.Instructions));
    }

    /// <summary>A run reaches two agents, so each is a named operation of its own rather than one.</summary>
    [Fact]
    public void Compose_TheComposingAgent_IsNamedApartFromEveryOtherAgent()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("{}");

        // Act
        var agent = AgentOver(chatClient);

        // Assert
        Assert.Equal(DiscoveryCompositionAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(DiscoveryPlanningAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(MailAnsweringAgentComposition.AgentName, agent.Name);
    }

    private static ChatClientAgent AgentOver(ScriptedChatClient chatClient) =>
        DiscoveryCompositionAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
}
