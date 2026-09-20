// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.CalendarEvents;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.CalendarEvents;

/// <summary>Covers what the extraction agent is composed as: its name, its instruction, and what it may reach.</summary>
/// <remarks>
/// Run over a substituted chat client rather than described, so what is proved is the composition the framework
/// actually received.
/// </remarks>
public sealed class CalendarEventExtractionAgentCompositionTests
{
    private const string Answer = """{ "events": [ { "title": "Site visit", "start": "2026-09-24T10:00" } ] }""";

    /// <summary>A tool would be a way to reach mail beyond the text, or a way to write to somebody's calendar.</summary>
    [Fact]
    public async Task Compose_TheExtractionAgent_OffersNoToolAtAll()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync("Sentence: lunch tomorrow", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(chatClient.Calls, call => Assert.True(call.Options?.Tools is null or []));
    }

    [Fact]
    public async Task Compose_TheExtractionAgent_CarriesItsOwnInstructionInsideTheEnvelope()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync("Sentence: lunch tomorrow", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            chatClient.Calls,
            call => Assert.Equal(CalendarEventExtractionInstructions.Text, call.Options?.Instructions));
    }

    /// <summary>One name for both halves, because they are one reading put to two inputs.</summary>
    [Fact]
    public void Compose_TheExtractionAgent_IsNamedApartFromEveryOtherAgent()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("{}");

        // Act
        var agent = AgentOver(chatClient);

        // Assert
        Assert.Equal(CalendarEventExtractionAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(MailAnsweringAgentComposition.AgentName, agent.Name);
    }

    private static ChatClientAgent AgentOver(ScriptedChatClient chatClient) =>
        CalendarEventExtractionAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
}
