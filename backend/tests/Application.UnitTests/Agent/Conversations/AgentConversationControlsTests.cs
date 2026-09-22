// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Localization;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Conversations;

/// <summary>Covers what each control writes into a conversation and what it tells a client about the write.</summary>
/// <remarks>
/// The store is a substitute because what is asserted here is the decision each control takes and the announcement it
/// makes; the statements that hold the conversation's row, and the refusals they produce, are the store's and are proved
/// against a real database.
/// </remarks>
public sealed class AgentConversationControlsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly AgentConversationId Conversation = AgentConversationId.New();

    private static readonly AgentMessageId Run = AgentMessageId.New();

    private readonly FakeTimeProvider clock = new(Now);

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly IUserLanguages languages = Substitute.For<IUserLanguages>();

    /// <summary>Every language the deployment writes for a person in, which a stopped answer's note has to exist in.</summary>
    public static TheoryData<UserLanguage> EveryLanguage => [.. Enum.GetValues<UserLanguage>()];

    /// <summary>A question is written as the person's own and opens an answer, and the client is told where the conversation reached under that answer.</summary>
    [Fact]
    public async Task AskAsync_AQuestionTheStoreWrites_OpensAnAnswerAndAnnouncesTheRunItOpened()
    {
        // Arrange
        await using var signals = this.Signals(out var channel);
        AgentMessageWritten? written = null;
        this.store
            .AskAsync(Conversation, SyntheticUser.Deployment, Arg.Do<AgentMessageWritten>(entry => written = entry), Arg.Any<AgentMessageId>(), Now, Arg.Any<CancellationToken>())
            .Returns(call => new AgentMessagePosting(AgentMessagePostingOutcome.Written, 2, call.ArgAt<AgentMessageId>(3)));
        var message = AgentMessageId.New();

        // Act
        var posting = await this.Controls(signals).AskAsync(
            Conversation,
            SyntheticUser.Deployment,
            message,
            PresentationText.Create("What did the supplier quote?"),
            AgentMessageScope.Mailbox(),
            TestContext.Current.CancellationToken);
        var announced = await this.PublishedAsync(signals, channel);

        // Assert
        Assert.NotNull(written);
        Assert.Equal((message, AgentMessageAuthor.Person), (written.MessageId, written.Author));
        Assert.NotNull(posting.Answer);
        var signal = Assert.Single(announced);
        Assert.Equal(
            (ClientSignalKind.RunAdvanced, Conversation, posting.Answer.Value.Value, 2L),
            (signal.Kind, signal.Conversation!.Value, signal.Run!.Value, signal.Sequence));
    }

    /// <summary>A post that wrote nothing new says nothing, because a client already told about the first one has nothing further to read.</summary>
    [Theory]
    [InlineData(AgentMessagePostingOutcome.AlreadyWritten)]
    [InlineData(AgentMessagePostingOutcome.AnswerInProgress)]
    [InlineData(AgentMessagePostingOutcome.NoSuchConversation)]
    [InlineData(AgentMessagePostingOutcome.ConversationFull)]
    public async Task AskAsync_APostThatWroteNothingNew_AnnouncesNothing(AgentMessagePostingOutcome outcome)
    {
        // Arrange
        await using var signals = this.Signals(out var channel);
        this.store
            .AskAsync(Conversation, SyntheticUser.Deployment, Arg.Any<AgentMessageWritten>(), Arg.Any<AgentMessageId>(), Now, Arg.Any<CancellationToken>())
            .Returns(new AgentMessagePosting(outcome, outcome is AgentMessagePostingOutcome.AlreadyWritten ? 2 : 0, Run));

        // Act
        await this.Controls(signals).AskAsync(
            Conversation,
            SyntheticUser.Deployment,
            AgentMessageId.New(),
            PresentationText.Create("What did the supplier quote?"),
            scope: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(await this.PublishedAsync(signals, channel));
    }

    /// <summary>An instruction is the person's own, names no scope of its own, and is announced under the run it steers.</summary>
    [Fact]
    public async Task SteerAsync_AnInstructionTheStoreWrites_AnnouncesItUnderTheRunItSteers()
    {
        // Arrange
        await using var signals = this.Signals(out var channel);
        AgentMessageWritten? written = null;
        this.store
            .SteerAsync(Conversation, SyntheticUser.Deployment, Run, Arg.Do<AgentMessageWritten>(entry => written = entry), Now, Arg.Any<CancellationToken>())
            .Returns(new AgentMessagePosting(AgentMessagePostingOutcome.Written, 7, Run));

        // Act
        await this.Controls(signals).SteerAsync(
            Conversation,
            SyntheticUser.Deployment,
            Run,
            AgentMessageId.New(),
            PresentationText.Create("Only the ones from this month."),
            TestContext.Current.CancellationToken);
        var announced = await this.PublishedAsync(signals, channel);

        // Assert
        Assert.NotNull(written);
        Assert.Equal((AgentMessageAuthor.Person, (AgentMessageScope?)null), (written.Author, written.Scope));
        var signal = Assert.Single(announced);
        Assert.Equal((Conversation, Run.Value, 7L), (signal.Conversation!.Value, signal.Run!.Value, signal.Sequence));
    }

    /// <summary>A stopped answer ends with the agent saying so in the person's own language, announced as one advance at the note's place.</summary>
    [Theory]
    [MemberData(nameof(EveryLanguage))]
    public async Task StopAsync_ARunningAnswer_EndsItWithTheAgentsNoteInThePersonsLanguage(UserLanguage language)
    {
        // Arrange
        await using var signals = this.Signals(out var channel);
        this.languages.LanguageOf(SyntheticUser.Deployment).Returns(language);
        AgentMessageWritten? note = null;
        this.store
            .StopAsync(Conversation, SyntheticUser.Deployment, Run, Arg.Do<AgentMessageWritten>(written => note = written), Now, Arg.Any<CancellationToken>())
            .Returns(6L);

        // Act
        var stopping = await this.Controls(signals).StopAsync(
            Conversation,
            SyntheticUser.Deployment,
            Run,
            TestContext.Current.CancellationToken);
        var announced = await this.PublishedAsync(signals, channel);

        // Assert
        Assert.Equal(AgentRunStopping.Stopped, stopping);
        Assert.NotNull(note);
        Assert.Equal(AgentMessageAuthor.Agent, note.Author);
        Assert.Equal(ApplicationTexts.GetText(ApplicationText.AgentRunStoppedNote, language), note.Text.Value);
        var signal = Assert.Single(announced);
        Assert.Equal((Run.Value, 6L), (signal.Run!.Value, signal.Sequence));
    }

    /// <summary>The note is the person's language rather than one fixed text, so two languages read differently.</summary>
    [Fact]
    public async Task StopAsync_ForTwoLanguages_WritesTwoDifferentNotes()
    {
        // Arrange
        await using var signals = this.Signals(out _);
        List<AgentMessageWritten> notes = [];
        this.store
            .StopAsync(Conversation, SyntheticUser.Deployment, Run, Arg.Do<AgentMessageWritten>(notes.Add), Now, Arg.Any<CancellationToken>())
            .Returns(6L);
        var controls = this.Controls(signals);

        // Act
        this.languages.LanguageOf(SyntheticUser.Deployment).Returns(UserLanguage.English);
        await controls.StopAsync(Conversation, SyntheticUser.Deployment, Run, TestContext.Current.CancellationToken);
        this.languages.LanguageOf(SyntheticUser.Deployment).Returns(UserLanguage.Polish);
        await controls.StopAsync(Conversation, SyntheticUser.Deployment, Run, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, notes.Count);
        Assert.NotEqual(notes[0].Text.Value, notes[1].Text.Value);
    }

    /// <summary>An answer that is not being composed writes nothing further, and the person is told whether the conversation is theirs at all.</summary>
    [Theory]
    [InlineData(true, AgentRunStopping.NotRunning)]
    [InlineData(false, AgentRunStopping.NoSuchConversation)]
    public async Task StopAsync_AnAnswerNoLongerComposed_WritesNothingFurtherAndSaysWhy(
        bool conversationIsTheirs,
        AgentRunStopping expected)
    {
        // Arrange
        await using var signals = this.Signals(out var channel);
        this.store
            .StopAsync(Conversation, SyntheticUser.Deployment, Run, Arg.Any<AgentMessageWritten>(), Now, Arg.Any<CancellationToken>())
            .Returns((long?)null);
        this.store
            .ReadAsync(Conversation, SyntheticUser.Deployment, 0, 1, Arg.Any<CancellationToken>())
            .Returns(conversationIsTheirs ? new AgentConversationReading(null, Now, false, [], false) : null);

        // Act
        var stopping = await this.Controls(signals).StopAsync(
            Conversation,
            SyntheticUser.Deployment,
            Run,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, stopping);
        Assert.Empty(await this.PublishedAsync(signals, channel));
    }

    /// <summary>An answer to a proposal belongs to no run, so it is announced naming the conversation alone.</summary>
    [Fact]
    public async Task DeclineProposalAsync_ADeclineTheStoreRecords_AnnouncesTheConversationWithNoRun()
    {
        // Arrange
        await using var signals = this.Signals(out var channel);
        this.store
            .TryResolveProposalAsync(Conversation, SyntheticUser.Deployment, 4, AgentProposalState.Declined, Now, Arg.Any<CancellationToken>())
            .Returns(9L);

        // Act
        var place = await this.Controls(signals).DeclineProposalAsync(
            Conversation,
            SyntheticUser.Deployment,
            proposedAt: 4,
            TestContext.Current.CancellationToken);
        var announced = await this.PublishedAsync(signals, channel);

        // Assert
        Assert.Equal(9L, place);
        var signal = Assert.Single(announced);
        Assert.Equal((Conversation, (Guid?)null, 9L), (signal.Conversation!.Value, signal.Run, signal.Sequence));
    }

    private AgentConversationControls Controls(ClientSignals signals) =>
        new(this.store, signals, this.languages, this.clock);

    private ClientSignals Signals(out RecordingClientSignalChannel channel)
    {
        channel = new RecordingClientSignalChannel();

        return new ClientSignals([channel], this.clock);
    }

    private async Task<IReadOnlyList<ClientSignal>> PublishedAsync(ClientSignals signals, RecordingClientSignalChannel channel)
    {
        this.clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        return [.. channel.Published];
    }
}
