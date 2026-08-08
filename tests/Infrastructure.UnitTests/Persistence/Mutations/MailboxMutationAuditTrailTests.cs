// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Mutations;

/// <summary>Covers the one rule that decides whether a finished mutation's history is kept, and what a lost entry costs.</summary>
/// <remarks>
/// The append itself is a database write and is proven against real PostgreSQL. What is here is the part above it: an
/// account that never asked for a trail is written nothing, and an append that does not happen may not fail the mutation
/// that has already changed somebody's mailbox — but has to be visible.
/// </remarks>
public sealed class MailboxMutationAuditTrailTests : IDisposable
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX"));

    private static readonly RemoteFolderPath ArchivePath = RemoteFolderPath.Create("Archive", '/');

    private readonly RecordingLoggerProvider logs = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly IMailboxMutationAuditEntryStore store = Substitute.For<IMailboxMutationAuditEntryStore>();
    private readonly MailboxMutationAuditTrail trail;

    public MailboxMutationAuditTrailTests()
    {
        this.loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(this.logs));

        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

        this.trail = new MailboxMutationAuditTrail(
            this.store,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                TimeProvider.System),
            new FakeTimeProvider(RecordedAt.AddMinutes(4)),
            new MailboxMutationAuditTelemetry(
                this.loggerFactory.CreateLogger<MailboxMutationAuditTelemetry>()));
    }

    public void Dispose()
    {
        this.loggerFactory.Dispose();
        this.logs.Dispose();
    }

    /// <summary>An account that never asked for a history accumulates none, which is what off by default has to mean.</summary>
    [Fact]
    public async Task RecordAsync_MutationOfAnUnauditedAccount_AppendsNothing()
    {
        // Act
        await this.trail.RecordAsync(
            CompletedRelocation() with { IsAudited = false },
            Inbox,
            TestContext.Current.CancellationToken);

        // Assert
        await this.store.DidNotReceive().AppendAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailboxMutationAuditEntry>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An audited mutation states its whole act to the store, once.</summary>
    [Fact]
    public async Task RecordAsync_AuditedMutation_AppendsTheEntryThatMutationStates()
    {
        // Arrange
        var appended = new List<MailboxMutationAuditEntry>();
        this.store
            .When(candidate => candidate.AppendAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailboxMutationAuditEntry>(),
                Arg.Any<CancellationToken>()))
            .Do(call => appended.Add(call.ArgAt<MailboxMutationAuditEntry>(1)!));

        // Act
        await this.trail.RecordAsync(CompletedRelocation(), Inbox, TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(appended);
        Assert.Equal(
            (MailboxMutation.Relocate, Account, Inbox.RemotePath, MailboxMutationAuditOutcome.Performed, RecordedAt),
            (entry.Mutation, entry.AccountId, entry.SourceFolderPath, entry.Outcome, entry.RequestedAt));
    }

    /// <summary>A trail that cannot be written costs the history and never the change that had already been made.</summary>
    [Fact]
    public async Task RecordAsync_StoreThatRefusesTheAppend_CompletesAndReportsTheLostEntry()
    {
        // Arrange
        this.store.AppendAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailboxMutationAuditEntry>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The trail is not reachable."));

        // Act
        var failure = await Record.ExceptionAsync(
            () => this.trail.RecordAsync(CompletedRelocation(), Inbox, TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(failure);
        Assert.Contains(
            this.logs.Records,
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("audit trail did not keep", StringComparison.Ordinal));
    }

    /// <summary>A cancellation leaves the entry just as missing, so it is reported before it travels on.</summary>
    [Fact]
    public async Task RecordAsync_CallerCancelsTheAppend_ReportsTheLostEntryAndRethrows()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => this.trail.RecordAsync(CompletedRelocation(), Inbox, cancellation.Token));

        // Assert
        Assert.Contains(
            this.logs.Records,
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("audit trail did not keep", StringComparison.Ordinal));
    }

    private static MailboxMutationRecord CompletedRelocation()
    {
        var occurrence = EmailOccurrenceId.Create(
            Account,
            Inbox.Id,
            ImapUidValidity.Create(1),
            ImapUid.Create(41));

        return new MailboxMutationRecord
        {
            Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(RecordedAt)),
            Request = MailboxMutationRequest.Relocate(
                StoredEmailId.Create(Guid.CreateVersion7(RecordedAt)),
                occurrence,
                MailboxMutationRequester.Rule("file-newsletters", 3),
                ArchivePath),
            Stage = MailboxMutationStage.Completed,
            IsAudited = true,
            RequiresSourceRemoval = true,
            Placement = RemoteEmailPlacement.NotReported(),
            AttemptCount = 1,
            RecordedAt = RecordedAt,
            StageChangedAt = RecordedAt.AddMinutes(4),
            LastFailure = (MailFathomErrorCode?)null,
            PlacementObservedAt = null,
            SourceRemovalObservedAt = null,
        };
    }
}
