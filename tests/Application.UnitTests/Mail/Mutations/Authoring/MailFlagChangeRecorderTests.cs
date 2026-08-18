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

    private static MailFlagChangeRecorder RecorderOver(
        InMemoryMailboxMutationRecordStore records,
        AuthoredMailboxTarget? target,
        AccessAuthorization? authorization = null)
    {
        var accountCatalog = Substitute.For<IMailAccountCatalog>();
        accountCatalog.ServedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

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
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()));
    }

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias)
    {
        var folder = MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder);
    }

    /// <summary>A session that commits, which is what a call writing several records has to be given.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
