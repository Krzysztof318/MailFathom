// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Signals;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers what a run's tool loop leaves in the conversation's technical history.</summary>
public sealed class RecordedChatClientTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] Senders = ["Northwind"];

    private readonly FakeTimeProvider clock = new(Now);

    private readonly AgentConversationId conversation = AgentConversationId.New();

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly IChatClient inner = Substitute.For<IChatClient>();

    private readonly RecordingClientSignalChannel channel = new();

    private readonly List<AgentConversationEntry> written = [];

    private readonly ClientSignals signals;

    /// <summary>Arranges a model that asks for one tool and then answers, charged on both calls.</summary>
    public RecordedChatClientTests()
    {
        this.signals = new ClientSignals([this.channel], this.clock);
        this.store
            .AppendAsync(this.conversation, SyntheticUser.Deployment, Arg.Any<AgentConversationEntry>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                this.written.Add(call.ArgAt<AgentConversationEntry>(2));

                return (long?)this.written.Count;
            });
        this.inner
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                Charged(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "search_mail", new Dictionary<string, object?> { ["query"] = "quote" })])), 300),
                Charged(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Northwind sent it.")), 420));
    }

    /// <summary>The call, its result, and what the first call was charged are written in the order the loop ran them, and announce nothing.</summary>
    [Fact]
    public async Task GetResponseAsync_AToolLoop_RecordsEveryStepInTheTechnicalHistoryWithoutAnnouncingIt()
    {
        // Arrange
        using var journal = this.Journal();
        using var client = new RecordedChatClient(this.inner, journal);
        ChatMessage question = new(ChatRole.User, "Who sent the quote?");
        ChatMessage called = new(ChatRole.Assistant, [new FunctionCallContent("call-1", "search_mail")]);
        ChatMessage answered = new(ChatRole.Tool, [new FunctionResultContent("call-1", Senders)]);

        // Act
        await client.GetResponseAsync([question], cancellationToken: TestContext.Current.CancellationToken);
        await client.GetResponseAsync([question, called, answered], cancellationToken: TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Collection(
            this.written,
            entry =>
            {
                var charge = Assert.IsType<AgentModelCharged>(entry);
                Assert.Equal((19L, 300L), (charge.SentCharacters, charge.InputTokens));
            },
            entry =>
            {
                var call = Assert.IsType<AgentToolCalled>(entry);
                Assert.Equal(("call-1", "search_mail", "{\"query\":\"quote\"}"), (call.CallId, call.ToolName, call.Arguments));
            },
            entry =>
            {
                var result = Assert.IsType<AgentToolAnswered>(entry);
                Assert.Equal(("call-1", "[\"Northwind\"]"), (result.CallId, result.Result));
            });
        Assert.Empty(this.channel.Published);
    }

    /// <summary>A first call the provider reported no usage for leaves no charge, because a later one carries the run's own tool traffic.</summary>
    [Fact]
    public async Task GetResponseAsync_AFirstCallReportingNoUsage_RecordsNoChargeFromALaterOne()
    {
        // Arrange
        this.inner
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "Looking.")),
                Charged(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Northwind sent it.")), 420));
        using var journal = this.Journal();
        using var client = new RecordedChatClient(this.inner, journal);
        ChatMessage question = new(ChatRole.User, "Who sent the quote?");

        // Act
        await client.GetResponseAsync([question], cancellationToken: TestContext.Current.CancellationToken);
        await client.GetResponseAsync([question], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.written.OfType<AgentModelCharged>());
    }

    /// <summary>A tool that handed the model a very long text is recorded at the length the record keeps, never whole.</summary>
    [Fact]
    public async Task GetResponseAsync_AResultPastTheBound_IsRecordedCutToIt()
    {
        // Arrange
        using var journal = this.Journal();
        using var client = new RecordedChatClient(this.inner, journal);
        ChatMessage answered = new(ChatRole.Tool, [new FunctionResultContent("call-1", new string('x', AgentConversationBounds.MaximumToolTextLength * 2))]);

        // Act
        await client.GetResponseAsync([answered], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentConversationBounds.MaximumToolTextLength, Assert.Single(this.written.OfType<AgentToolAnswered>()).Result.Length);
    }

    /// <summary>A write the conversation refuses is the stop reaching the run, so the call is abandoned rather than sent.</summary>
    [Fact]
    public async Task GetResponseAsync_AResultTheConversationRefuses_AbandonsTheCall()
    {
        // Arrange
        this.store
            .AppendAsync(this.conversation, SyntheticUser.Deployment, Arg.Any<AgentConversationEntry>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((long?)null);
        using var journal = this.Journal();
        using var client = new RecordedChatClient(this.inner, journal);
        ChatMessage answered = new(ChatRole.Tool, [new FunctionResultContent("call-1", "[]")]);

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync([answered], cancellationToken: TestContext.Current.CancellationToken));
        await this.inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.inner.Dispose();

        return this.signals.DisposeAsync();
    }

    private static ChatResponse Charged(ChatResponse response, long inputTokens)
    {
        response.Usage = new UsageDetails { InputTokenCount = inputTokens };

        return response;
    }

    private AgentAnswerJournal Journal() => new(
        this.conversation,
        SyntheticUser.Deployment,
        AgentMessageId.New(),
        openedAt: 1,
        this.store,
        this.signals,
        Substitute.For<IUserLanguages>(),
        this.clock);
}
