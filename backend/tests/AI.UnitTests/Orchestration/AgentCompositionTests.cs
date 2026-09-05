// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the one composition every AI operation is built by: what it names the run, what it offers it, and what instruction it carries.</summary>
/// <remarks>
/// Each test runs the composed agent over a substituted chat client and reads what the client was asked to send, so what
/// is proved is the composition rather than a description of it.
/// </remarks>
public sealed class AgentCompositionTests
{
    private const string Instruction = "Answer the question you were asked.";

    /// <summary>The whole of what this build ships: a composition over the registered default is the operation's own text.</summary>
    [Fact]
    public async Task Compose_TheEmptyEnvelope_CarriesTheOperationsInstructionUnchanged()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, []),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Instruction, Assert.Single(chatClient.Calls).Options?.Instructions);
    }

    /// <summary>Concatenated and nothing else: the operation's text sits between the two halves, with no separator of the composition's own.</summary>
    [Fact]
    public async Task Compose_AnEnvelopeSupplyingBothHalves_PlacesThemFirstAndLast()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, []),
            new StubAgentInstructionEnvelope("before. ", " after."),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            $"before. {Instruction} after.",
            Assert.Single(chatClient.Calls).Options?.Instructions);
    }

    /// <summary>Either half stands on its own, so an implementation with something to say in one position says nothing in the other.</summary>
    [Theory]
    [InlineData("before. ", "", "before. Answer the question you were asked.")]
    [InlineData("", " after.", "Answer the question you were asked. after.")]
    public async Task Compose_AnEnvelopeSupplyingOneHalf_PlacesThatHalfAlone(
        string preamble,
        string postamble,
        string expected)
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, []),
            new StubAgentInstructionEnvelope(preamble, postamble),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, Assert.Single(chatClient.Calls).Options?.Instructions);
    }

    /// <summary>
    /// Asked at composition rather than read once at start, which is what lets an implementation vary its answer per
    /// person or per request without any operation changing.
    /// </summary>
    [Fact]
    public async Task Compose_AnEnvelopeAnsweringDifferentlyEachTime_IsConsultedOncePerComposition()
    {
        // Arrange
        var envelope = new CountingAgentInstructionEnvelope();
        using var firstClient = ScriptedChatClient.Answering("answered");
        using var secondClient = ScriptedChatClient.Answering("answered");
        var operation = new AgentOperation("an-operation", Instruction, []);

        // Act
        var first = AgentComposition.Compose(
            firstClient,
            ChatDeclarations.Plan(),
            operation,
            envelope,
            NullLoggerFactory.Instance);
        await first.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        var second = AgentComposition.Compose(
            secondClient,
            ChatDeclarations.Plan(),
            operation,
            envelope,
            NullLoggerFactory.Instance);
        await second.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"1. {Instruction}", Assert.Single(firstClient.Calls).Options?.Instructions);
        Assert.Equal($"2. {Instruction}", Assert.Single(secondClient.Calls).Options?.Instructions);
    }

    /// <summary>
    /// The name a run reports and the tool set that is the operation's whole capability, both taken from the operation
    /// rather than decided by the composition: an operation that only reads is one declaring no tool that mutates.
    /// </summary>
    [Fact]
    public async Task Compose_AnOperation_ReportsItsNameAndOffersItsOwnToolSet()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var tool = AIFunctionFactory.Create(() => "nothing", "count_nothing");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, [tool]),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("an-operation", agent.Name);
        Assert.Equal(
            ["count_nothing"],
            (Assert.Single(chatClient.Calls).Options?.Tools ?? []).Select(offered => offered.Name));
    }

    /// <summary>An envelope with something to say in both positions.</summary>
    private sealed class StubAgentInstructionEnvelope(string preamble, string postamble) : IAgentInstructionEnvelope
    {
        public string Preamble => preamble;

        public string Postamble => postamble;
    }

    /// <summary>An envelope whose answer changes on every reading, which is what a per-person implementation would look like from here.</summary>
    private sealed class CountingAgentInstructionEnvelope : IAgentInstructionEnvelope
    {
        private int readings;

        public string Preamble => $"{++this.readings}. ";

        public string Postamble => string.Empty;
    }
}
