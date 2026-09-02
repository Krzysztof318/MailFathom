// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers what a caller may take back, what it may not, and which grant each entry point asks for.</summary>
/// <remarks>
/// The whole point of the use case is that a change nothing has been asked of a server for is still the caller's to
/// stop, and that one past that point is reported where it stands rather than declared void.
/// </remarks>
public sealed class MailboxChangeWithdrawerTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset WithdrawnAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    /// <summary>A pending change is the caller's to stop, and the account's next pass never sees it.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_APendingFlagChange_MovesItToWithdrawn()
    {
        // Arrange
        var opened = await this.OpenAsync(FlagRequestIn(Inbox, uid: 7));
        var withdrawer = this.Withdrawer();

        // Act
        var withdrawn = await withdrawer.WithdrawFlagChangesAsync(
            [opened.Id],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(withdrawn);

        Assert.Equal(opened.Id, entry.RecordId);
        Assert.Equal(MailboxMutationLifecycle.Cancelled, entry.Lifecycle);
        Assert.Empty(await this.records.ReadOutstandingAsync(Account, 10, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A command already issued cannot be recalled, so a record past the stage it was written down at is reported where
    /// it stands rather than refused — which is also what makes the call safe to repeat.
    /// </summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_AChangeAlreadyIssuedToTheServer_ReportsItWhereItStands()
    {
        // Arrange
        var request = FlagRequestIn(Inbox, uid: 7);
        var opened = await this.OpenAsync(request);
        this.records.Arrange(request, record => record with { Stage = MailboxMutationStage.PlacementIssued });
        var withdrawer = this.Withdrawer();

        // Act
        var withdrawn = await withdrawer.WithdrawFlagChangesAsync(
            [opened.Id],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(withdrawn);

        Assert.NotEqual(MailboxMutationLifecycle.Cancelled, entry.Lifecycle);
        Assert.True(entry.IsOutcomeUnknown);
    }

    /// <summary>The grant that authored a change is the grant that withdraws it, so the flag entry point says nothing about a move.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_ARecordNamingAMove_LeavesItAloneAndAbsentFromTheAnswer()
    {
        // Arrange
        var move = await this.OpenAsync(RelocateRequestIn(Inbox, uid: 9));
        var withdrawer = this.Withdrawer(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite, MailFathomPermission.MailMove));

        // Act
        var withdrawn = await withdrawer.WithdrawFlagChangesAsync(
            [move.Id],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(withdrawn);
        Assert.Single(await this.records.ReadOutstandingAsync(Account, 10, TestContext.Current.CancellationToken));
    }

    /// <summary>The moving entry point is the counterpart, and it says nothing about a flag change.</summary>
    [Fact]
    public async Task WithdrawMovesAsync_AMoveAndAFlagChange_WithdrawsTheMoveAlone()
    {
        // Arrange
        var move = await this.OpenAsync(RelocateRequestIn(Inbox, uid: 9));
        var flagChange = await this.OpenAsync(FlagRequestIn(Inbox, uid: 7));
        var withdrawer = this.Withdrawer(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite, MailFathomPermission.MailMove));

        // Act
        var withdrawn = await withdrawer.WithdrawMovesAsync(
            [move.Id, flagChange.Id],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(withdrawn);

        Assert.Equal(move.Id, entry.RecordId);
        Assert.Equal(MailboxMutationLifecycle.Cancelled, entry.Lifecycle);
        Assert.Equal(
            flagChange.Id,
            Assert.Single(await this.records.ReadOutstandingAsync(Account, 10, TestContext.Current.CancellationToken))
                .Record.Id);
    }

    /// <summary>A record recorded in a folder the caller may no longer read is absent, the same answer a read of that folder's mail gives.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_ARecordInAFolderWithheldFromTools_LeavesItAlone()
    {
        // Arrange
        var withheld = await this.OpenAsync(FlagRequestIn(Withheld, uid: 11));
        var withdrawer = this.Withdrawer();

        // Act
        var withdrawn = await withdrawer.WithdrawFlagChangesAsync(
            [withheld.Id],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(withdrawn);
        Assert.Single(await this.records.ReadOutstandingAsync(Account, 10, TestContext.Current.CancellationToken));
    }

    /// <summary>Withdrawing needs authority over the same kind of change, because the worst it does is stop one.</summary>
    [Fact]
    public async Task WithdrawMovesAsync_ACallerHoldingOnlyTheFlagGrant_IsRefused()
    {
        // Arrange
        var move = await this.OpenAsync(RelocateRequestIn(Inbox, uid: 9));
        var withdrawer = this.Withdrawer(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            withdrawer.WithdrawMovesAsync([move.Id], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailMove, refusal.RequiredPermission);
        Assert.Single(await this.records.ReadOutstandingAsync(Account, 10, TestContext.Current.CancellationToken));
    }

    /// <summary>The caller supplies the identities, so without a ceiling the size of one call would be the caller's to choose.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_MoreRecordsThanOneCallMayWithdraw_IsRefused()
    {
        // Arrange
        var withdrawer = this.Withdrawer();
        var asked = Enumerable
            .Range(0, MailboxChangeWithdrawer.MaximumRecordsPerCall + 1)
            .Select(_ => MailboxMutationRecordId.Create(Guid.CreateVersion7()))
            .ToArray();

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            withdrawer.WithdrawFlagChangesAsync(asked, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("recordIds.Count", thrown.ParamName);
    }

    /// <summary>Withdrawing a change that is already withdrawn is the repeat the design admits, and it changes nothing.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_AChangeAlreadyWithdrawn_ReportsItWithoutChangingIt()
    {
        // Arrange
        var opened = await this.OpenAsync(FlagRequestIn(Inbox, uid: 7));
        var withdrawer = this.Withdrawer();
        var first = Assert.Single(
            await withdrawer.WithdrawFlagChangesAsync([opened.Id], TestContext.Current.CancellationToken));

        // Act
        var second = Assert.Single(
            await withdrawer.WithdrawFlagChangesAsync([opened.Id], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailboxMutationLifecycle.Cancelled, second.Lifecycle);
        Assert.Equal(first.StageChangedAt, second.StageChangedAt);
    }

    private static MailboxMutationRequest FlagRequestIn(MailFolderAlias folderAlias, uint uid) =>
        MailboxMutationRequest.SetSeen(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Account.Owner,
            OccurrenceIn(folderAlias, uid),
            Requester,
            isSeen: true);

    private static MailboxMutationRequest RelocateRequestIn(MailFolderAlias folderAlias, uint uid) =>
        MailboxMutationRequest.Relocate(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Account.Owner,
            OccurrenceIn(folderAlias, uid),
            Requester,
            RemoteFolderPath.Create("Archive"));

    private static EmailOccurrenceId OccurrenceIn(MailFolderAlias folderAlias, uint uid) => EmailOccurrenceId.Create(
        Account.Id,
        MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value)).Id,
        ImapUidValidity.Create(42),
        ImapUid.Create(uid));

    /// <summary>Writes one record down, as an authoring use case would have, so a withdrawal has something to take back.</summary>
    private Task<MailboxMutationRecord> OpenAsync(MailboxMutationRequest request) => this.records.OpenAsync(
        CommittingSession(),
        request,
        TestContext.Current.CancellationToken);

    private static IPersistenceSession CommittingSession()
    {
        var session = Substitute.For<IPersistenceSession>();
        session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

        return session;
    }

    private MailboxChangeWithdrawer Withdrawer(AccessAuthorization? authorization = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite);

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => CommittingSession());

        return new MailboxChangeWithdrawer(
            callerAuthorization,
            new MailboxScopeResolver(
                OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account.Id)),
                StubMailFolderParticipation
                    .Mapping(new MailFolderIdentity(Account.Id, Inbox))
                    .Hiding(new MailFolderIdentity(Account.Id, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            this.records,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(WithdrawnAt)));
    }
}
