// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Search;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.Agent.Conversations;
using MailFathom.Application.UnitTests.Agent.Search;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Answering;

/// <summary>Covers how one answer is run: what reaches the composer, and what the answer ends in however the run went.</summary>
/// <remarks>
/// The store is a substitute that records every entry written, because what is asserted is the record a person reads
/// afterwards — whether the answer ended, and how — rather than the statements that hold it.
/// </remarks>
public sealed class AgentAnsweringTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly AgentConversationId Conversation = AgentConversationExample.Conversation;

    private readonly FakeTimeProvider clock = new(Now);

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly IAgentAnswerComposer composer = Substitute.For<IAgentAnswerComposer>();

    private readonly IAgentConversationSummarizer summarizer = Substitute.For<IAgentConversationSummarizer>();

    private readonly IMailAnsweringSpendLedger spendLedger = Substitute.For<IMailAnsweringSpendLedger>();

    private readonly IUserLanguages languages = Substitute.For<IUserLanguages>();

    private readonly IAgentConversationSearchIndex searchIndex = Substitute.For<IAgentConversationSearchIndex>();

    private readonly List<AgentConversationEntry> written = [];

    private readonly AgentMessageId earlierQuestion = AgentMessageId.New();

    private readonly AgentMessageId question = AgentMessageId.New();

    private readonly AgentMessageId answer = AgentMessageId.New();

    private readonly ClientSignals signals;

    private AgentContextBudget budget = AgentContextBudget.Default;

    private ActiveEmbeddingSpace space = EmbeddingSpaceExample.Inactive();

    private bool stopped;

    /// <summary>Arranges a conversation of one earlier exchange and the question now being answered.</summary>
    public AgentAnsweringTests()
    {
        this.signals = new ClientSignals([new RecordingClientSignalChannel()], this.clock);
        this.spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        this.store
            .AppendAsync(Conversation, SyntheticUser.Deployment, Arg.Any<AgentConversationEntry>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (this.stopped)
                {
                    return (long?)null;
                }

                this.written.Add(call.ArgAt<AgentConversationEntry>(2));

                return 5L + this.written.Count;
            });
        this.store
            .ReadAsync(Conversation, SyntheticUser.Deployment, AgentConversationHistory.Technical, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(
                Title: null,
                Now,
                Composing: true,
                AgentConversationExample.Written(
                    AgentConversationExample.Question(this.earlierQuestion, "Who sent the quote?"),
                    new AgentAnswerStarted(AgentMessageId.New()),
                    AgentConversationExample.Note(AgentMessageId.New(), "Northwind sent it."),
                    AgentConversationExample.Question(this.question, "Accept it for me."),
                    new AgentAnswerStarted(this.answer)),
                MoreFollows: false));
    }

    /// <summary>An answer the composer finishes ends as completed, and an untitled conversation is named after the question.</summary>
    [Fact]
    public async Task RunAsync_AComposerThatFinishes_EndsTheAnswerCompleted()
    {
        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentAnswerOutcome.Completed, Assert.IsType<AgentAnswerEnded>(this.written[^1]).Outcome);
        await this.store.Received(1).TrySetTitleAsync(
            Conversation,
            SyntheticUser.Deployment,
            PresentationText.Create("Accept it for me."),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Once the answer has ended, what the turn said is placed for the history search.</summary>
    [Fact]
    public async Task RunAsync_AnswerEnded_EmbedsTheQuestionForTheHistorySearch()
    {
        // Arrange
        this.space = EmbeddingSpaceExample.Serving(EmbeddingSpaceExample.Generator());
        this.store
            .ReadAsync(Conversation, SyntheticUser.Deployment, AgentConversationHistory.Visible, 3, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(
                Title: null,
                Now,
                Composing: false,
                [
                    AgentConversationExample.Question(this.question, "Accept it for me.") with { Sequence = 4 },
                    new AgentAnswerStarted(this.answer) { Sequence = 5 },
                    new AgentAnswerEnded(this.answer, AgentAnswerOutcome.Completed) { Sequence = 6 },
                ],
                MoreFollows: false));

        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        await this.searchIndex.Received(1).SaveEmbeddingAsync(
            Conversation,
            SyntheticUser.Deployment,
            4,
            EmbeddingSpaceExample.Profile,
            Arg.Any<EmbeddingVector>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The composer is handed the conversation before the question and not the question itself, which it is handed on its own.</summary>
    [Fact]
    public async Task RunAsync_EarlierTurns_ReachTheComposerWithoutTheQuestion()
    {
        // Arrange
        AgentAnswerBrief? brief = null;
        await this.composer.ComposeAsync(Arg.Do<AgentAnswerBrief>(handed => brief = handed), Arg.Any<AgentAnswerJournal>(), Arg.Any<CancellationToken>());

        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(brief);
        Assert.Equal(
            [(AgentMessageAuthor.Person, "Who sent the quote?"), (AgentMessageAuthor.Agent, "Northwind sent it.")],
            brief.History.Select(static turn => (turn.Author, turn.Text)));
    }

    /// <summary>A period already spent ends the answer as failed before anything is composed.</summary>
    [Fact]
    public async Task RunAsync_APeriodAlreadySpent_EndsTheAnswerFailedWithoutComposing()
    {
        // Arrange
        this.spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentAnswerOutcome.Failed, Assert.IsType<AgentAnswerEnded>(this.written[^1]).Outcome);
        await this.composer.DidNotReceive().ComposeAsync(Arg.Any<AgentAnswerBrief>(), Arg.Any<AgentAnswerJournal>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A deployment with no chat endpoint ends every answer as failed, and spends nothing doing so.</summary>
    [Fact]
    public async Task RunAsync_NoComposer_EndsTheAnswerFailedWithoutSpending()
    {
        // Act
        await this.Answering(composing: false).RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentAnswerOutcome.Failed, Assert.IsType<AgentAnswerEnded>(this.written[^1]).Outcome);
        await this.spendLedger.DidNotReceive().TryAdmitRunAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A stop the person recorded ends the run and writes no ending of its own, the stop having written it.</summary>
    [Fact]
    public async Task RunAsync_AStopRecordedMeanwhile_WritesNoEnding()
    {
        // Arrange
        this.composer
            .ComposeAsync(Arg.Any<AgentAnswerBrief>(), Arg.Any<AgentAnswerJournal>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var journal = call.ArgAt<AgentAnswerJournal>(1);
                this.stopped = true;

                await journal.ReportAsync(AgentActivity.SearchingMail, CancellationToken.None);
                journal.Stopping.ThrowIfCancellationRequested();
            });

        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(this.written, static entry => entry is AgentAnswerEnded);
    }

    /// <summary>A fault nothing has a name for still leaves the answer ended, and is raised for the caller to report.</summary>
    [Fact]
    public async Task RunAsync_AnUnnamedFault_EndsTheAnswerFailedAndIsRaised()
    {
        // Arrange
        this.composer
            .ComposeAsync(Arg.Any<AgentAnswerBrief>(), Arg.Any<AgentAnswerJournal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Unexpected.")));

        // Act and assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken));
        Assert.Equal(AgentAnswerOutcome.Failed, Assert.IsType<AgentAnswerEnded>(this.written[^1]).Outcome);
    }

    /// <summary>A conversation past the budget is summarised before the turn, and the summary is recorded beside what it covers rather than in its place.</summary>
    [Fact]
    public async Task RunAsync_AConversationPastTheBudget_RecordsACompactionAndLeadsTheTurnWithIt()
    {
        // Arrange
        this.LongConversation();
        this.summarizer
            .SummarizeAsync(null, Arg.Any<IReadOnlyList<AgentHistoryTurn>>(), Arg.Any<CancellationToken>())
            .Returns("They asked twice about the quote.");
        AgentAnswerBrief? brief = null;
        await this.composer.ComposeAsync(Arg.Do<AgentAnswerBrief>(handed => brief = handed), Arg.Any<AgentAnswerJournal>(), Arg.Any<CancellationToken>());

        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        var compaction = Assert.Single(this.written.OfType<AgentConversationCompacted>());
        Assert.Equal(this.answer, compaction.MessageId);
        Assert.Equal("They asked twice about the quote.", compaction.Summary);
        Assert.NotNull(brief);
        Assert.EndsWith("They asked twice about the quote.", Assert.Single(brief.History).Text, StringComparison.Ordinal);
    }

    /// <summary>A summariser that produced nothing does not fail the turn: it sends the newest turns that fit, and records no summary.</summary>
    [Fact]
    public async Task RunAsync_ASummarizerThatFails_SendsTheNewestTurnsThatFitAndRecordsNothing()
    {
        // Arrange
        this.LongConversation();
        this.summarizer
            .SummarizeAsync(Arg.Any<string?>(), Arg.Any<IReadOnlyList<AgentHistoryTurn>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        AgentAnswerBrief? brief = null;
        await this.composer.ComposeAsync(Arg.Do<AgentAnswerBrief>(handed => brief = handed), Arg.Any<AgentAnswerJournal>(), Arg.Any<CancellationToken>());

        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(this.written, static entry => entry is AgentConversationCompacted);
        Assert.Equal(AgentAnswerOutcome.Completed, Assert.IsType<AgentAnswerEnded>(this.written[^1]).Outcome);
        Assert.NotNull(brief);
        Assert.Equal([new string('c', 3_000)], brief.History.Select(static turn => turn.Text));
    }

    /// <summary>A conversation inside the budget is sent whole and costs no summary.</summary>
    [Fact]
    public async Task RunAsync_AConversationInsideTheBudget_AsksForNoSummary()
    {
        // Act
        await this.Answering().RunAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        await this.summarizer.DidNotReceive().SummarizeAsync(
            Arg.Any<string?>(), Arg.Any<IReadOnlyList<AgentHistoryTurn>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A follow-up that states no scope is asked under the one the conversation stands under, which no summary may paraphrase away.</summary>
    [Fact]
    public async Task RunAsync_AFollowUpStatingNoScope_IsAskedUnderTheScopeInForce()
    {
        // Arrange
        AgentAnswerBrief? brief = null;
        await this.composer.ComposeAsync(Arg.Do<AgentAnswerBrief>(handed => brief = handed), Arg.Any<AgentAnswerJournal>(), Arg.Any<CancellationToken>());

        // Act
        await this.Answering().RunAsync(this.Question() with { Scope = null }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(brief);
        Assert.Equal(AgentScopeKind.Mailbox, brief.Question.Scope?.Kind);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => this.signals.DisposeAsync();

    /// <summary>Makes the conversation before the question three long turns, which a budget of a thousand tokens cannot send whole.</summary>
    private void LongConversation()
    {
        this.budget = new AgentContextBudget(AgentContextBudget.MinimumTokens);
        this.store
            .ReadAsync(Conversation, SyntheticUser.Deployment, AgentConversationHistory.Technical, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(
                Title: null,
                Now,
                Composing: true,
                AgentConversationExample.Written(
                    AgentConversationExample.Question(AgentMessageId.New(), new string('a', 3_000)),
                    AgentConversationExample.Note(AgentMessageId.New(), new string('b', 3_000)),
                    AgentConversationExample.Question(AgentMessageId.New(), new string('c', 3_000)),
                    AgentConversationExample.Question(this.question, "Accept it for me."),
                    new AgentAnswerStarted(this.answer)),
                MoreFollows: false));
    }

    private AgentQuestion Question() => new(
        Conversation,
        SyntheticUser.Deployment,
        PresentationText.Create("Accept it for me."),
        AgentMessageScope.Mailbox(),
        this.answer,
        OpenedAt: 5,
        Now);

    private AgentAnswering Answering(bool composing = true) => new(
        this.store,
        composing ? this.composer : null,
        this.summarizer,
        new AgentConversationEmbedding(this.store, this.space, this.searchIndex),
        this.budget,
        this.spendLedger,
        this.signals,
        this.languages,
        this.clock);
}
