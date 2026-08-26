// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders;

/// <summary>Covers what happens to the mail of a folder an operator has stopped mirroring.</summary>
public sealed class UnmirroredMailFolderEraserTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("primary"));
    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    /// <summary>The pass runs inside one transaction, so what it removed is either all committed or not removed at all.</summary>
    [Fact]
    public async Task EraseAsync_AFolderHoldingStoredMail_ErasesItInsideOneCommittedTransaction()
    {
        // Arrange
        var store = new RecordingMirrorStore(new MailFolderMirrorErasure(ErasedEmailCount: 12, EmailsRemain: false));
        var eraser = EraserOver(store, maxEmailsPerPass: 500);

        // Act
        var erasure = await eraser.EraseAsync(Account, Junk, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(12, erasure.ErasedEmailCount);
        Assert.False(erasure.EmailsRemain);
        Assert.Equal([(Account, Junk, 500)], store.Passes);
    }

    /// <summary>A mailbox's worth of rows is not one transaction, so a pass that filled its bound says another is owed.</summary>
    [Fact]
    public async Task EraseAsync_MoreMailThanOnePassMayErase_ReportsThatEmailsRemain()
    {
        // Arrange
        var store = new RecordingMirrorStore(new MailFolderMirrorErasure(ErasedEmailCount: 500, EmailsRemain: true));
        var eraser = EraserOver(store, maxEmailsPerPass: 500);

        // Act
        var erasure = await eraser.EraseAsync(Account, Junk, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(erasure.EmailsRemain);
    }

    /// <summary>A folder that never stored anything costs one bounded query and reports nothing, which is every run after the first.</summary>
    [Fact]
    public async Task EraseAsync_AFolderThatStoredNothing_ErasesNothing()
    {
        // Arrange
        var store = new RecordingMirrorStore(MailFolderMirrorErasure.Nothing);
        var eraser = EraserOver(store, maxEmailsPerPass: 500);

        // Act
        var erasure = await eraser.EraseAsync(Account, Junk, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, erasure.ErasedEmailCount);
        Assert.False(erasure.EmailsRemain);
    }

    /// <summary>The bound is the one the backward pass over stored mail already carries, rather than a second setting nobody configured.</summary>
    [Fact]
    public async Task EraseAsync_TheConfiguredBound_IsWhatBoundsOnePass()
    {
        // Arrange
        var store = new RecordingMirrorStore(MailFolderMirrorErasure.Nothing);
        var eraser = EraserOver(store, maxEmailsPerPass: 25);

        // Act
        await eraser.EraseAsync(Account, Junk, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([(Account, Junk, 25)], store.Passes);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task EraseAsync_ACallerGrantedOnlyTheAdministrativeOperate_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var store = new RecordingMirrorStore(new MailFolderMirrorErasure(ErasedEmailCount: 0, EmailsRemain: false));
        var eraser = EraserOver(
            store,
            maxEmailsPerPass: 500,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            eraser.EraseAsync(Account, Junk, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
        Assert.Empty(store.Passes);
    }

    private static UnmirroredMailFolderEraser EraserOver(
        IStoredMailFolderMirrorStore store,
        int maxEmailsPerPass,
        AccessAuthorization? authorization = null)
    {
        var clock = new FakeTimeProvider();
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new UnmirroredMailFolderEraser(
            store,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
            new MailboxSynchronizationOptions { MaxReconciledEmailsPerRun = maxEmailsPerPass },
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminErase));
    }

    /// <summary>Records what each pass was asked to erase, and answers with the erasure the test arranged.</summary>
    private sealed class RecordingMirrorStore(MailFolderMirrorErasure erasure) : IStoredMailFolderMirrorStore
    {
        private readonly List<(MailAccountIdentity Account, MailFolderAlias FolderAlias, int MaxEmails)> passes =
            [];

        public IReadOnlyList<(MailAccountIdentity Account, MailFolderAlias FolderAlias, int MaxEmails)> Passes =>
            this.passes;

        public Task<MailFolderMirrorErasure> EraseFolderMirrorAsync(
            IPersistenceSession session,
            MailAccountIdentity account,
            MailFolderAlias folderAlias,
            int maxEmails,
            CancellationToken cancellationToken)
        {
            this.passes.Add((account, folderAlias, maxEmails));

            return Task.FromResult(erasure);
        }
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
