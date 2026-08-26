// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Answering;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Answering;

/// <summary>Covers what one finished run leaves behind: who is owed an entry, what it says, and what a lost one costs.</summary>
/// <remarks>
/// The append itself is a database write and is proven against real PostgreSQL. What is here is the part above it: which
/// accounts of a run's scope owe an entry, that the entry names the mail of its own account and marks what the answer
/// cited, and that a record that could not be written may not fail the question it describes — but has to be visible.
/// </remarks>
public sealed class MailAnsweringAuditTrailTests : IDisposable
{
    private const string EndpointAlias = "answering";
    private const string InstructionsVersion = "0a1b2c3d4e5f";

    private static readonly DateTimeOffset StartedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailAccountId Personal = MailAccountId.Create("personal");

    private readonly RecordingLoggerProvider logs = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly IMailAnsweringAuditEntryStore store = Substitute.For<IMailAnsweringAuditEntryStore>();
    private readonly IMailAnsweringAuditSettingsReader settings =
        Substitute.For<IMailAnsweringAuditSettingsReader>();

    private readonly MailAnsweringAuditTrail trail;

    public MailAnsweringAuditTrailTests()
    {
        this.loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(this.logs));

        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

        this.settings.GetAnsweringAuditSettings(Arg.Any<MailAccountId>())
            .Returns(MailAnsweringAuditSettings.Disabled);

        this.trail = new MailAnsweringAuditTrail(
            this.settings,
            this.store,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                TimeProvider.System),
            new MailAnsweringAuditTelemetry(
                this.loggerFactory.CreateLogger<MailAnsweringAuditTelemetry>()));
    }

    public void Dispose()
    {
        this.loggerFactory.Dispose();
        this.logs.Dispose();
    }

    /// <summary>An account that never asked for a record accumulates none, which is what off by default has to mean.</summary>
    [Fact]
    public async Task RecordAsync_ARunOverAccountsThatKeepNoRecord_AppendsNothing()
    {
        // Arrange
        var observation = AnsweredRun([Work], PassageOf(Work, 1));

        // Act
        await this.trail.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Assert
        await this.store.DidNotReceive().AppendAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailAnsweringAuditEntry>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The entry a clean run leaves: what it read, which of that the answer named, and what produced it.</summary>
    [Fact]
    public async Task RecordAsync_AnAnsweredRun_AppendsWhatItReadAndWhatTheAnswerCited()
    {
        // Arrange
        this.RecordFor(Work);
        var read = PassageOf(Work, 1);
        var cited = PassageOf(Work, 2);
        var appended = this.CapturingAppends();
        var observation = AnsweredRun([Work], read, cited);
        observation.RecordOutcome(
            MailAnsweringRunOutcome.Answered,
            [cited.StoredEmailId],
            StartedAt.AddSeconds(9));

        // Act
        await this.trail.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(appended);

        Assert.Equal(
            (Work, EndpointAlias, InstructionsVersion, MailAnsweringRunOutcome.Answered, StartedAt.AddSeconds(9)),
            (entry.AccountId, entry.ChatEndpointAlias, entry.InstructionsVersion, entry.Outcome, entry.CompletedAt));
        Assert.Equal(
            [(read.StoredEmailId, 0, false), (cited.StoredEmailId, 1, true)],
            entry.Emails.Select(email => (email.StoredEmailId, email.Position, email.WasCited)));
    }

    /// <summary>Each degradation reaches the entry as itself, and two of them reach it together.</summary>
    [Theory]
    [InlineData(MailAnsweringRunDegradation.None)]
    [InlineData(MailAnsweringRunDegradation.RetrievalCeilingReached)]
    [InlineData(MailAnsweringRunDegradation.RelevanceFilterFellBack)]
    [InlineData(MailAnsweringRunDegradation.RetrievalCeilingReached | MailAnsweringRunDegradation.RelevanceFilterFellBack)]
    public async Task RecordAsync_ADegradedRun_AppendsTheDegradationItReached(MailAnsweringRunDegradation degradation)
    {
        // Arrange
        this.RecordFor(Work);
        var appended = this.CapturingAppends();
        var observation = AnsweredRun([Work], degradation, PassageOf(Work, 1));

        // Act
        await this.trail.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(degradation, Assert.Single(appended).Degradation);
    }

    /// <summary>A run that never produced an answer still read mail, and that is exactly what its record has to say.</summary>
    [Fact]
    public async Task RecordAsync_ARunThatEndedWithoutAnAnswer_AppendsWhatItHadAlreadyRead()
    {
        // Arrange
        this.RecordFor(Work);
        var appended = this.CapturingAppends();
        var read = PassageOf(Work, 1);
        var observation = AnsweredRun([Work], read);
        observation.RecordOutcome(MailAnsweringRunOutcome.ProviderFailed, [], StartedAt.AddSeconds(3));

        // Act
        await this.trail.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(appended);

        Assert.Equal(MailAnsweringRunOutcome.ProviderFailed, entry.Outcome);
        Assert.Equal([(read.StoredEmailId, false)], entry.Emails.Select(email => (email.StoredEmailId, email.WasCited)));
    }

    /// <summary>
    /// One entry per account in the run's scope, each naming only its own account's mail. A question asked across two
    /// mailboxes must not tell either operator what the other's holds.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ARunOverTwoAccounts_AppendsOneEntryEachNamingOnlyItsOwnMail()
    {
        // Arrange
        this.RecordFor(Work);
        this.RecordFor(Personal);
        var appended = this.CapturingAppends();
        var observation = AnsweredRun([Work, Personal], PassageOf(Work, 1), PassageOf(Personal, 2));

        // Act
        await this.trail.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Assert
        // The scope orders its accounts, so the entries arrive in that order rather than in the order the run read them.
        Assert.Equal(
            [(Personal, EmailIdentityAt(2)), (Work, EmailIdentityAt(1))],
            appended.Select(entry => (entry.AccountId, Assert.Single(entry.Emails).StoredEmailId.Value)));
        Assert.Single(appended.Select(entry => entry.RunId).Distinct());
    }

    /// <summary>An account in scope that the run drew nothing from is still told a question was asked of its mailbox.</summary>
    [Fact]
    public async Task RecordAsync_AnAccountTheRunReadNothingFrom_AppendsAnEntryNamingNoMail()
    {
        // Arrange
        this.RecordFor(Work);
        this.RecordFor(Personal);
        var appended = this.CapturingAppends();
        var observation = AnsweredRun([Work, Personal], PassageOf(Work, 1));

        // Act
        await this.trail.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(appended.Single(entry => entry.AccountId == Personal).Emails);
    }

    /// <summary>A record that cannot be written costs the history and never the answer that was already produced.</summary>
    [Fact]
    public async Task RecordAsync_StoreThatRefusesTheAppend_CompletesAndReportsTheLostEntries()
    {
        // Arrange
        this.RecordFor(Work);
        this.store.AppendAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAnsweringAuditEntry>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The record is not reachable."));

        // Act
        var failure = await Record.ExceptionAsync(() => this.trail.RecordAsync(
            AnsweredRun([Work], PassageOf(Work, 1)),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(failure);
        Assert.Contains(
            this.logs.Records,
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("answering record did not keep", StringComparison.Ordinal));
    }

    /// <summary>A cancellation leaves the entries just as missing, so they are reported before it travels on.</summary>
    [Fact]
    public async Task RecordAsync_CallerCancelsTheAppend_ReportsTheLostEntriesAndRethrows()
    {
        // Arrange
        this.RecordFor(Work);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => this.trail.RecordAsync(
            AnsweredRun([Work], PassageOf(Work, 1)),
            cancellation.Token));

        // Assert
        Assert.Contains(
            this.logs.Records,
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("answering record did not keep", StringComparison.Ordinal));
    }

    /// <summary>Names one email by its position, so the same run of a test always uses the same identifiers.</summary>
    private static Guid EmailIdentityAt(int position) => new($"00000000-0000-0000-0000-{position:D12}");

    private static EmailKnowledgePassage PassageOf(MailAccountId accountId, int position) => new()
    {
        StoredEmailId = StoredEmailId.Create(EmailIdentityAt(position)),
        AccountId = accountId,
        FolderAlias = MailFolderAlias.Create("inbox"),
        Subject = "Quarterly invoice",
        ReceivedAt = StartedAt,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = "the invoice is attached",
    };

    private static MailAnsweringRunObservation AnsweredRun(
        IReadOnlyList<MailAccountId> accountIds,
        params EmailKnowledgePassage[] passages) =>
        AnsweredRun(accountIds, MailAnsweringRunDegradation.None, passages);

    private static MailAnsweringRunObservation AnsweredRun(
        IReadOnlyList<MailAccountId> accountIds,
        MailAnsweringRunDegradation degradation,
        params EmailKnowledgePassage[] passages)
    {
        var observation = new MailAnsweringRunObservation(
            MailAnsweringRunId.Create(Guid.CreateVersion7(StartedAt)),
            MailboxScope.Create(SyntheticMailOwner.Deployment, accountIds, []),
            StartedAt);

        observation.RecordComposition(EndpointAlias, InstructionsVersion);
        observation.RecordRetrieval(new MailAnsweringRetrievalReport(
            passages,
            passages.Length,
            passages.Length,
            degradation));
        observation.RecordOutcome(MailAnsweringRunOutcome.Answered, [], StartedAt.AddSeconds(9));

        return observation;
    }

    /// <summary>Turns the record on for one account, leaving every other account at the disabled default.</summary>
    private void RecordFor(MailAccountId accountId) =>
        this.settings.GetAnsweringAuditSettings(accountId)
            .Returns(new MailAnsweringAuditSettings(IsEnabled: true, TimeSpan.FromDays(30)));

    /// <summary>Keeps every entry the trail staged, in the order it staged them.</summary>
    private List<MailAnsweringAuditEntry> CapturingAppends()
    {
        var appended = new List<MailAnsweringAuditEntry>();

        this.store
            .When(candidate => candidate.AppendAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAnsweringAuditEntry>(),
                Arg.Any<CancellationToken>()))
            .Do(call => appended.Add(call.ArgAt<MailAnsweringAuditEntry>(1)!));

        return appended;
    }
}
