// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers the grant, the visibility rule, and the records one caller's flag change is written down as.</summary>
public sealed class MailFlagChangeRecorderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    /// <summary>Each value asked for is written down as its own durable record, which is the unit convergence carries.</summary>
    [Fact]
    public async Task RecordAsync_EveryValueAsked_OpensOneRecordPerValue()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(records, TargetIn(Inbox));
        var change = AuthoredMailFlagChange.Create(
            LocalEmail,
            seen: true,
            flagged: true,
            MailKeywordChangeDirection.Add,
            ["$Todo"]);

        // Act
        var result = await recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [MailboxMutation.SetSeen, MailboxMutation.SetFlagged, MailboxMutation.AddKeywords],
            result.Recorded.Select(recorded => recorded.Mutation));
        Assert.Equal(3, records.OpenedRecordCount);
        Assert.All(result.Recorded, recorded => Assert.Equal(MailboxMutationLifecycle.Pending, recorded.Lifecycle));
    }

    /// <summary>A record names the occurrence a command will be issued against, which the caller never supplied.</summary>
    [Fact]
    public async Task RecordAsync_AChange_RecordsItAgainstTheOccurrenceTheEmailIsAt()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var target = TargetIn(Inbox);
        var recorder = RecorderOver(records, target);
        var change = AuthoredMailFlagChange.Create(LocalEmail, seen: true, null, null, null);

        // Act
        var result = await recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(records.OpenedRequests);
        Assert.Equal(target.Occurrence, request.Occurrence);
        Assert.Equal(LocalEmail, request.StoredEmailId);
        Assert.True(request.DesiredSeenState);
        Assert.Equal(Account, result.AccountId);
        Assert.Equal(Inbox, result.FolderAlias);
    }

    /// <summary>Reaching a mailbox at all is the grant's question, asked before the email is looked up.</summary>
    [Fact]
    public async Task RecordAsync_ACallerWithoutTheWritingGrant_IsRefusedWithoutWritingAnything()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(
            records,
            TargetIn(Inbox),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
        var change = AuthoredMailFlagChange.Create(LocalEmail, seen: true, null, null, null);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailFlagsWrite, refusal.RequiredPermission);
        Assert.Equal(0, records.OpenedRecordCount);
    }

    /// <summary>A folder no tool may read is a folder no tool may write, or the write surface would be the way round the withholding.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailInAFolderWithheldFromTools_IsRefusedAsNotFound()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(records, TargetIn(Withheld));
        var change = AuthoredMailFlagChange.Create(LocalEmail, seen: true, null, null, null);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeTargetNotFoundException>(() =>
            recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, refusal.ErrorCode);
        Assert.Equal(0, records.OpenedRecordCount);
    }

    /// <summary>An email no row carries answers exactly as a withheld one does, so asking cannot reveal which identifiers exist.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailThisDeploymentHoldsNoRowFor_IsRefusedAsNotFound()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(records, target: null);
        var change = AuthoredMailFlagChange.Create(LocalEmail, seen: true, null, null, null);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeTargetNotFoundException>(() =>
            recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, refusal.ErrorCode);
        Assert.Equal(0, records.OpenedRecordCount);
    }

    /// <summary>A retry under one identity is one change, because the record store admits one per occurrence, requester, and mutation.</summary>
    [Fact]
    public async Task RecordAsync_TheSameChangeAskedTwiceUnderOneRequester_OpensOneRecord()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(records, TargetIn(Inbox));
        var change = AuthoredMailFlagChange.Create(LocalEmail, null, flagged: true, null, null);

        // Act
        var first = await recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken);
        var second = await recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, records.OpenedRecordCount);
        Assert.Equal(
            Assert.Single(first.Recorded).RecordId,
            Assert.Single(second.Recorded).RecordId);
    }

    /// <summary>A second invocation is a second request, which is what lets a caller star a message, unstar it, and star it again.</summary>
    [Fact]
    public async Task RecordAsync_TheSameChangeUnderASecondInvocation_OpensASecondRecord()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(records, TargetIn(Inbox));
        var change = AuthoredMailFlagChange.Create(LocalEmail, null, flagged: true, null, null);

        // Act
        await recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken);
        await recorder.RecordAsync(
            change,
            MailboxMutationRequester.Command("call-2"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, records.OpenedRecordCount);
    }

    /// <summary>The same request identity asking for the opposite value is refused rather than answered with the earlier record.</summary>
    /// <remarks>
    /// The idempotency identity carries the occurrence, the requester, and the mutation, and none of the three carries
    /// the value. Reporting the first record as this call's would tell a caller its unstar was written down while the
    /// star stays on the message, and nothing the caller receives states the terms it would have to compare.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_TheSameRequestIdentityAskingForTheOppositeValue_IsRefused()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(records, TargetIn(Inbox));
        var starred = AuthoredMailFlagChange.Create(LocalEmail, null, flagged: true, null, null);
        var unstarred = AuthoredMailFlagChange.Create(LocalEmail, null, flagged: false, null, null);
        await recorder.RecordAsync(starred, Requester, TestContext.Current.CancellationToken);

        // Act
        var thrown = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(
            () => recorder.RecordAsync(unstarred, Requester, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, thrown.ErrorCode);
        Assert.Equal(1, records.OpenedRecordCount);
        Assert.True(Assert.Single(records.OpenedRequests).DesiredFlaggedState);
    }

    /// <summary>A conflicting commit re-opens every record, and the call still publishes one per value asked for.</summary>
    /// <remarks>
    /// The staging callback runs once per attempt, so an implementation that accumulated across attempts would publish
    /// the losing attempt's records beside the winner's — and both attempts open the same rows, so the answer would
    /// name each change twice with the suite otherwise green.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_ACommitThatConflictsBeforeItSucceeds_PublishesOneRecordPerValue()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var attempts = 0;
        var recorder = RecorderOver(
            records,
            TargetIn(Inbox),
            sessionFactory: () => new StubPersistenceSession(
                ++attempts == 1
                    ? PersistenceCommitResult.ConcurrencyConflict
                    : PersistenceCommitResult.Committed));
        var change = AuthoredMailFlagChange.Create(
            LocalEmail,
            seen: true,
            flagged: true,
            MailKeywordChangeDirection.Add,
            ["$Todo"]);

        // Act
        var result = await recorder.RecordAsync(change, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, attempts);
        Assert.Equal(
            [MailboxMutation.SetSeen, MailboxMutation.SetFlagged, MailboxMutation.AddKeywords],
            result.Recorded.Select(recorded => recorded.Mutation));
        Assert.Equal(3, records.OpenedRecordCount);
    }

    private static MailFlagChangeRecorder RecorderOver(
        InMemoryMailboxMutationRecordStore records,
        AuthoredMailboxTarget? target,
        AccessAuthorization? authorization = null,
        Func<IPersistenceSession>? sessionFactory = null)
    {
        var accountCatalog = Substitute.For<IMailAccountCatalog>();
        accountCatalog.ServedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => sessionFactory is null
                ? new StubPersistenceSession(PersistenceCommitResult.Committed)
                : sessionFactory());

        // A conflicting attempt waits out the policy's jittered backoff, which is at most 50 milliseconds for the first
        // one. The system clock is what makes that wait end: a fake would have to be advanced from outside the call, and
        // the advance would race the registration of the delay it is meant to end.
        return new MailFlagChangeRecorder(
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite),
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation
                    .Mapping(new MailFolderIdentity(Account, Inbox))
                    .Hiding(new MailFolderIdentity(Account, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            targets,
            records,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions(),
                sessionFactory is null ? new FakeTimeProvider() : TimeProvider.System));
    }

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias)
    {
        var folder = MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder);
    }

    /// <summary>A session that reports the outcome its attempt was arranged with, which is what drives the retry.</summary>
    private sealed class StubPersistenceSession(PersistenceCommitResult outcome) : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(outcome);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
