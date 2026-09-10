// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
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

/// <summary>Covers ending the wait a delete was opened under: who may ask, what it reaches, and what asking twice does.</summary>
/// <remarks>
/// It is the counterpart of <see cref="MailboxChangeWithdrawerTests" /> and asks the same questions of the opposite
/// answer — one closes the way back by taking the change, the other by letting it go — so the grant, the visibility
/// rule, and the bound are each asserted here as well rather than assumed to hold because they hold there.
/// </remarks>
public sealed class MailboxChangeReleaserTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("personal"));

    private static readonly MailFolderAlias Trash = MailFolderAlias.Create("Trash");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset ReleasedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    /// <summary>The wait ends and the account is woken, which together are the whole of what the client asked for.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_AHeldDelete_EndsItsWaitAndBringsTheAccountsRunForward()
    {
        // Arrange
        var request = DeleteRequestIn(Trash, uid: 7);
        var opened = await this.OpenHeldAsync(request);
        var runSignal = new MailAccountRunSignal();
        var releaser = this.Releaser(runSignal: runSignal);

        // Act
        var released = await releaser.ReleaseDeletesAsync([opened.Id], TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account.Id, TestContext.Current.CancellationToken);

        Assert.Equal(opened.Id, Assert.Single(released).RecordId);
        Assert.Null(this.records.HeldUntilOf(request));
        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>The stage is untouched, because what ended is the wait in front of the record rather than anything about it.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_AHeldDelete_LeavesItPending()
    {
        // Arrange
        var opened = await this.OpenHeldAsync(DeleteRequestIn(Trash, uid: 7));

        // Act
        var released = await this.Releaser().ReleaseDeletesAsync(
            [opened.Id],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxMutationLifecycle.Pending, Assert.Single(released).Lifecycle);
    }

    /// <summary>A client that asks twice, or asks after the window has already passed, is a client that asked once.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_ADeleteAlreadyReleased_ReportsItAgainWithoutChangingIt()
    {
        // Arrange
        var opened = await this.OpenHeldAsync(DeleteRequestIn(Trash, uid: 7));
        var releaser = this.Releaser();
        var first = Assert.Single(
            await releaser.ReleaseDeletesAsync([opened.Id], TestContext.Current.CancellationToken));

        // Act
        var second = Assert.Single(
            await releaser.ReleaseDeletesAsync([opened.Id], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(first.RecordId, second.RecordId);
        Assert.Equal(first.StageChangedAt, second.StageChangedAt);
    }

    /// <summary>A record already taken back is reported where it stands, which is what makes the two calls safe to race.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_ADeleteAlreadyWithdrawn_ReportsItAsCancelledAndWakesNobody()
    {
        // Arrange
        var request = DeleteRequestIn(Trash, uid: 7);
        var opened = await this.OpenHeldAsync(request);
        this.records.Arrange(request, record => record with { Stage = MailboxMutationStage.Cancelled });
        var runSignal = new MailAccountRunSignal();

        // Act
        var released = await this.Releaser(runSignal: runSignal).ReleaseDeletesAsync(
            [opened.Id],
            TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account.Id, TestContext.Current.CancellationToken);

        Assert.Equal(MailboxMutationLifecycle.Cancelled, Assert.Single(released).Lifecycle);
        Assert.False(waiting.Token.IsCancellationRequested);
    }

    /// <summary>The entry point holds authority over deletes and says nothing about a change of another kind.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_ARecordNamingAMove_LeavesItAloneAndAbsentFromTheAnswer()
    {
        // Arrange
        var move = await this.OpenHeldAsync(RelocateRequestIn(Trash, uid: 9));

        // Act
        var released = await this.Releaser().ReleaseDeletesAsync(
            [move.Id],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(released);
    }

    /// <summary>A record recorded in a folder the caller may no longer read is absent, the same answer a read of that folder's mail gives.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_ARecordInAFolderWithheldFromTools_LeavesItAlone()
    {
        // Arrange
        var request = DeleteRequestIn(Withheld, uid: 11);
        var withheld = await this.OpenHeldAsync(request);

        // Act
        var released = await this.Releaser().ReleaseDeletesAsync(
            [withheld.Id],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(released);
        Assert.NotNull(this.records.HeldUntilOf(request));
    }

    /// <summary>Releasing needs authority over the change it stops deferring, exactly as withdrawing one does.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_ACallerHoldingOnlyTheMovingGrant_IsRefused()
    {
        // Arrange
        var request = DeleteRequestIn(Trash, uid: 7);
        var opened = await this.OpenHeldAsync(request);
        var releaser = this.Releaser(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailMove));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            releaser.ReleaseDeletesAsync([opened.Id], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailDelete, refusal.RequiredPermission);
        Assert.NotNull(this.records.HeldUntilOf(request));
    }

    /// <summary>The caller supplies the identities, so without a ceiling the size of one call would be the caller's to choose.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_MoreRecordsThanOneCallMayRelease_IsRefused()
    {
        // Arrange
        var asked = Enumerable
            .Range(0, MailboxChangeReleaser.MaximumRecordsPerCall + 1)
            .Select(_ => MailboxMutationRecordId.Create(Guid.CreateVersion7()))
            .ToArray();

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            this.Releaser().ReleaseDeletesAsync(asked, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("recordIds.Count", thrown.ParamName);
    }

    private static MailboxMutationRequest DeleteRequestIn(MailFolderAlias folderAlias, uint uid) =>
        MailboxMutationRequest.Delete(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Account.User,
            OccurrenceIn(folderAlias, uid),
            Requester,
            AuthoredDeleteEmailDisposition.RetainTombstone);

    private static MailboxMutationRequest RelocateRequestIn(MailFolderAlias folderAlias, uint uid) =>
        MailboxMutationRequest.Relocate(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Account.User,
            OccurrenceIn(folderAlias, uid),
            Requester,
            RemoteFolderPath.Create("Archive"));

    private static EmailOccurrenceId OccurrenceIn(MailFolderAlias folderAlias, uint uid) => EmailOccurrenceId.Create(
        Account.Id,
        MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value)).Id,
        ImapUidValidity.Create(42),
        ImapUid.Create(uid));

    private static IPersistenceSession CommittingSession()
    {
        var session = Substitute.For<IPersistenceSession>();
        session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

        return session;
    }

    /// <summary>Writes one record down held, as the client's own delete route would have, so there is a wait to end.</summary>
    private Task<MailboxMutationRecord> OpenHeldAsync(MailboxMutationRequest request) => this.records.OpenAsync(
        CommittingSession(),
        request,
        ReleasedAt.AddSeconds(15),
        TestContext.Current.CancellationToken);

    private MailboxChangeReleaser Releaser(
        AccessAuthorization? authorization = null,
        MailAccountRunSignal? runSignal = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDelete);

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => CommittingSession());

        return new MailboxChangeReleaser(
            callerAuthorization,
            new MailboxScopeResolver(
                OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account.Id)),
                StubMailFolderParticipation
                    .Mapping(new MailFolderIdentity(Account.Id, Trash))
                    .Hiding(new MailFolderIdentity(Account.Id, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            this.records,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(ReleasedAt)),
            runSignal ?? new MailAccountRunSignal());
    }
}
