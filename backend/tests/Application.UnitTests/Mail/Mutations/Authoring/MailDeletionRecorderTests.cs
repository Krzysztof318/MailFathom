// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations;
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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers the deleting grant, the visibility rule the message is judged by, and the record a delete is written down as.</summary>
/// <remarks>
/// It is the shorter half of <see cref="MailRelocationRecorderTests" />, and shorter for one reason: a delete names no
/// folder, so nothing here is about a destination. What is left is who may ask, which mail this caller may ask about,
/// and what the account says becomes of the local copy.
/// </remarks>
public sealed class MailDeletionRecorderTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("personal"));

    private static readonly MailFolderAlias Trash = MailFolderAlias.Create("Trash");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly IAuthoredDeleteEmailDispositionReader dispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    /// <summary>Arranges the disposition every test shares, so a test that is about it overrides it afterwards.</summary>
    public MailDeletionRecorderTests() => this.dispositions
        .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
        .Returns(AuthoredDeleteEmailDisposition.RetainLocalCopy);

    /// <summary>The record names the occurrence a command will be issued against, and names no folder in either direction.</summary>
    [Fact]
    public async Task RecordAsync_ADeleteOfReadableMail_OpensOneRecordAgainstTheOccurrence()
    {
        // Arrange
        var target = TargetIn(Trash);
        var recorder = this.Recorder(target);

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.Recorded, result.Outcome);
        Assert.Equal(MailboxMutationLifecycle.Pending, result.Lifecycle);

        var request = Assert.Single(this.records.OpenedRequests);

        Assert.Equal(MailboxMutation.Delete, request.Mutation);
        Assert.Equal(target.Occurrence, request.Occurrence);
        Assert.Null(request.DestinationPath);
    }

    /// <summary>
    /// The local copy follows the account's configured posture, and it is written onto the record while the
    /// configuration still says it rather than read again in the pass that carries the delete out.
    /// </summary>
    [Theory]
    [InlineData(AuthoredDeleteEmailDisposition.RetainLocalCopy)]
    [InlineData(AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData(AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    public async Task RecordAsync_ADeleteOfReadableMail_RecordsTheAccountsConfiguredDisposition(
        AuthoredDeleteEmailDisposition configured)
    {
        // Arrange
        this.dispositions.GetAuthoredDeleteDisposition(Account.Id).Returns(configured);
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(configured, Assert.Single(this.records.OpenedRequests).LocalDisposition);
    }

    /// <summary>
    /// Deleting mail is its own grant, so a caller holding the one that files mail elsewhere is refused. Filing a
    /// message in the trash is reversible; this expunges the occurrence and leaves nothing to fetch back.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ACallerHoldingOnlyTheMovingGrant_IsRefusedWithoutWritingAnything()
    {
        // Arrange
        var recorder = this.Recorder(
            TargetIn(Trash),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailMove));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailDelete, refusal.RequiredPermission);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A folder no tool may read is a folder no tool may delete mail out of, or the write surface would be the way round the withholding.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailInAFolderWithheldFromTools_ReportsTheMessageAsNotFound()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Withheld));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.MessageNotFound, result.Outcome);
        Assert.Null(result.RecordId);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>An email no row carries answers exactly as a withheld one does, so asking cannot reveal which identifiers exist.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailThisDeploymentHoldsNoRowFor_ReportsTheMessageAsNotFound()
    {
        // Arrange
        var recorder = this.Recorder(target: null);

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.MessageNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// An account removed from configuration between the read and the write leaves the disposition unanswerable, which
    /// is this caller's message to report rather than a fault that ends the batch.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnAccountThatLeftConfigurationMidRequest_ReportsItRatherThanFailing()
    {
        // Arrange
        this.dispositions
            .GetAuthoredDeleteDisposition(Account.Id)
            .Throws(new InvalidOperationException("No account carries the identifier personal."));
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.AccountNoLongerConfigured, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A retry under one identity is one delete, because the record store admits one per occurrence, requester, and mutation.</summary>
    [Fact]
    public async Task RecordAsync_TheSameDeleteAskedTwiceUnderOneRequester_OpensOneRecord()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        var first = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);
        var second = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.records.OpenedRecordCount);
        Assert.Equal(first.RecordId, second.RecordId);
    }

    /// <summary>Which folder the message is in decides nothing here, the client's rule being a sentence on a screen rather than a policy of the deployment's.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailOutsideTheTrash_IsRecordedLikeAnyOther()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(MailFolderAlias.Create("INBOX")));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.Recorded, result.Outcome);
        Assert.Equal(MailboxMutation.Delete, Assert.Single(this.records.OpenedRequests).Mutation);
    }

    /// <summary>A delete waits on the convergence pass exactly as a move does, so it must not sit there until the interval is out.</summary>
    [Fact]
    public async Task RecordAsync_ADelete_BringsTheAccountsNextSynchronizationRunForward()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.Recorder(TargetIn(Trash), runSignal: runSignal);

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account.Id, TestContext.Current.CancellationToken);

        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>
    /// A delete a person may still take back is written down held, and the account is deliberately left asleep: a pass
    /// brought forward now would issue the command while the way back is still on their screen.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADeleteCarryingAWithdrawalWindow_HoldsTheRecordAndLeavesTheAccountAsleep()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.Recorder(TargetIn(Trash), runSignal: runSignal);

        // Act
        await recorder.RecordAsync(
            LocalEmail,
            Requester,
            TimeSpan.FromSeconds(15),
            TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account.Id, TestContext.Current.CancellationToken);

        Assert.False(waiting.Token.IsCancellationRequested);
        Assert.Equal(
            RecordedAt + TimeSpan.FromSeconds(15),
            this.records.HeldUntilOf(Assert.Single(this.records.OpenedRequests)));
    }

    /// <summary>A caller naming no window is every caller but the client's own, and its delete waits for nothing.</summary>
    [Fact]
    public async Task RecordAsync_ADeleteCarryingNoWithdrawalWindow_HoldsTheRecordForNoTime()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(this.records.HeldUntilOf(Assert.Single(this.records.OpenedRequests)));
    }

    /// <summary>
    /// A zero window is no wait at all, so it is written down as the unheld record it is and brings the account forward
    /// like one — rather than held until the instant it was written, which a pass would find due and nothing would wake.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADeleteCarryingAZeroWithdrawalWindow_IsRecordedUnheldAndBringsTheRunForward()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.Recorder(TargetIn(Trash), runSignal: runSignal);

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account.Id, TestContext.Current.CancellationToken);

        Assert.True(waiting.Token.IsCancellationRequested);
        Assert.Null(this.records.HeldUntilOf(Assert.Single(this.records.OpenedRequests)));
    }

    /// <summary>
    /// The grant is asked before the window is measured, as a withdrawal asks it before it measures a batch, so a caller
    /// holding nothing is refused as unauthorized whatever else is wrong with what it sent.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ACallerWithoutTheGrantNamingANegativeWindow_IsRefusedAsUnauthorized()
    {
        // Arrange
        var recorder = this.Recorder(
            TargetIn(Trash),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => recorder.RecordAsync(
            LocalEmail,
            Requester,
            TimeSpan.FromSeconds(-1),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailDelete, refusal.RequiredPermission);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    private MailDeletionRecorder Recorder(
        AuthoredMailboxTarget? target,
        AccessAuthorization? authorization = null,
        MailAccountRunSignal? runSignal = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDelete);
        var accountCatalog = OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account.Id));

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailDeletionRecorder(
            callerAuthorization,
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation
                    .Mapping(
                        new MailFolderIdentity(Account.Id, Trash),
                        new MailFolderIdentity(Account.Id, MailFolderAlias.Create("INBOX")))
                    .Hiding(new MailFolderIdentity(Account.Id, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            targets,
            this.dispositions,
            this.records,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(RecordedAt)),
            runSignal ?? new MailAccountRunSignal(),
            new FakeTimeProvider(RecordedAt));
    }

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias)
    {
        var folder = MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value));

        return new AuthoredMailboxTarget(
            Account.User,
            EmailOccurrenceId.Create(Account.Id, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
