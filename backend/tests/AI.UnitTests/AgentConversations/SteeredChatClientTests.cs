// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Signals;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers how an instruction the person posts into a run reaches the model.</summary>
public sealed class SteeredChatClientTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider clock = new(Now);

    private readonly AgentConversationId conversation = AgentConversationId.New();

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly IChatClient inner = Substitute.For<IChatClient>();

    private readonly List<IReadOnlyList<ChatMessage>> sent = [];

    private readonly ClientSignals signals;

    /// <summary>Arranges a model that answers every call and records what each one was sent.</summary>
    public SteeredChatClientTests()
    {
        this.signals = new ClientSignals([new RecordingClientSignalChannel()], this.clock);
        this.inner
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                this.sent.Add([.. call.ArgAt<IEnumerable<ChatMessage>>(0)]);

                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done."));
            });
    }

    /// <summary>An instruction posted between two turns is sent after everything already exchanged, and on every turn after it.</summary>
    [Fact]
    public async Task GetResponseAsync_AnInstructionPostedBetweenTurns_IsSentAfterTheExchangeFromThenOn()
    {
        // Arrange
        this.store
            .ReadAsync(this.conversation, SyntheticUser.Deployment, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Reading(),
                Reading(new AgentMessageWritten(AgentMessageId.New(), AgentMessageAuthor.Person, PresentationText.Create("Only this week."), Scope: null) with { Sequence = 7 }),
                Reading());
        using var journal = this.Journal();
        using var client = new SteeredChatClient(this.inner, journal, SensitiveContentEgressGuards.Inactive());
        ChatMessage[] exchanged = [new(ChatRole.User, "What is due?")];

        // Act
        await client.GetResponseAsync(exchanged, cancellationToken: TestContext.Current.CancellationToken);
        await client.GetResponseAsync(exchanged, cancellationToken: TestContext.Current.CancellationToken);
        await client.GetResponseAsync(exchanged, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [["What is due?"], ["What is due?", "Only this week."], ["What is due?", "Only this week."]],
            this.sent.Select(static messages => messages.Select(static message => message.Text).ToArray()));
    }

    /// <summary>Releasing the steering releases nothing beneath it, so the run's budgeted client is left for its owner to release asynchronously.</summary>
    [Fact]
    public void Dispose_TheSteering_LeavesTheClientBeneathItUnreleased()
    {
        // Arrange
        using var journal = this.Journal();
        var client = new SteeredChatClient(this.inner, journal, SensitiveContentEgressGuards.Inactive());

        // Act
        client.Dispose();

        // Assert
        this.inner.DidNotReceive().Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.inner.Dispose();

        return this.signals.DisposeAsync();
    }

    private static AgentConversationReading Reading(params AgentConversationEntry[] entries) =>
        new(Title: null, Now, Composing: true, entries, MoreFollows: false);

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
