// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.BodyCleanup;

/// <summary>Covers what the body-cleanup agent is composed as: its name, its instruction, and what it may reach.</summary>
/// <remarks>
/// Run over a substituted chat client rather than described, so what is proved is the composition the framework actually
/// received.
/// </remarks>
public sealed class MailBodyCleanupAgentCompositionTests
{
    private const string Answer = """{"segments":[{"from":0,"to":0,"action":"keep"}]}""";

    /// <summary>The outline is put to the agent in the turn, so a tool would be a way to reach mail beyond the message asked about.</summary>
    [Fact]
    public async Task Compose_TheBodyCleanupAgent_OffersNoToolAtAll()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Blocks: 1\n0 paragraph links=0 | Your code is 558132",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(chatClient.Calls, call => Assert.True(call.Options?.Tools is null or []));
    }

    /// <summary>
    /// One instruction rather than one per language, which is this agent's difference from every other here: the answer
    /// carries no sentence, so there is no language for it to be written in.
    /// </summary>
    [Fact]
    public async Task Compose_TheBodyCleanupAgent_CarriesItsOneInstructionInsideTheEnvelope()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Blocks: 1\n0 paragraph links=0 | Your code is 558132",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            chatClient.Calls,
            call => Assert.Equal(MailBodyCleanupInstructions.Text, call.Options?.Instructions));
    }

    /// <summary>The name is what the composition is recorded under, so it is this agent's alone.</summary>
    [Fact]
    public void Compose_TheBodyCleanupAgent_IsNamedApartFromEveryOtherAgent()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);

        // Act
        var agent = AgentOver(chatClient);

        // Assert
        Assert.Equal(MailBodyCleanupAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(MailAnsweringAgentComposition.AgentName, agent.Name);
    }

    private static ChatClientAgent AgentOver(ScriptedChatClient chatClient) =>
        MailBodyCleanupAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
}
