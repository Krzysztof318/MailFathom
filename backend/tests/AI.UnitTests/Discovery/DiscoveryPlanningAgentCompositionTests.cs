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

/// <summary>Covers what the planning agent is composed as: its name, its instruction, and what it may reach.</summary>
/// <remarks>
/// Run over a substituted chat client rather than described, so what is proved is the composition the framework
/// actually received.
/// </remarks>
public sealed class DiscoveryPlanningAgentCompositionTests
{
    /// <summary>Deciding what to retrieve is a reading of the question, so the agent is shown nothing and reaches nothing.</summary>
    [Fact]
    public async Task Compose_ThePlanningAgent_OffersNoToolAtAll()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("""{"intent": "findFact", "lookups": []}""");
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Question: was the invoice attached",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(chatClient.Calls, call => Assert.True(call.Options?.Tools is null or []));
    }

    [Fact]
    public async Task Compose_ThePlanningAgent_CarriesItsOwnInstructionInsideTheEnvelope()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("""{"intent": "findFact", "lookups": []}""");
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Question: was the invoice attached",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            chatClient.Calls,
            call => Assert.Equal(DiscoveryPlanningInstructions.Text, call.Options?.Instructions));
    }

    /// <summary>A name of its own, so a run reaching two agents is two named operations rather than one.</summary>
    [Fact]
    public void Compose_ThePlanningAgent_IsNamedApartFromEveryOtherAgent()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("{}");

        // Act
        var agent = AgentOver(chatClient);

        // Assert
        Assert.Equal(DiscoveryPlanningAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(MailAnsweringAgentComposition.AgentName, agent.Name);
    }

    private static ChatClientAgent AgentOver(ScriptedChatClient chatClient) =>
        DiscoveryPlanningAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
}
