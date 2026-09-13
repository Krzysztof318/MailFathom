// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders.Local;

/// <summary>Covers the bounded passes that erase the mail of a held account's erased folders.</summary>
public sealed class LocalMailFolderMailErasureHandlerTests
{
    private const int MaxEmailsPerPass = 500;

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("primary"));

    private static readonly EraseLocalMailFolderMailJobPayload FirstPass =
        EraseLocalMailFolderMailJobPayload.For(Account, LocalMailFolderId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")));

    [Fact]
    public async Task RunAsync_APassThatLeavesNothing_ErasesUnderTheBoundAndQueuesNoOther()
    {
        // Arrange
        var store = StoreErasing(new LocalMailFolderMailErasure(ErasedEmailCount: 3, EmailsRemain: false));
        var jobs = Substitute.For<IJobStore>();
        var handler = HandlerOver(store, jobs);

        // Act
        await handler.RunAsync(FirstPass, TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).EraseMailOfErasedFoldersAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            MaxEmailsPerPass,
            Arg.Any<CancellationToken>());
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_APassThatLeavesMail_QueuesTheNextPass()
    {
        // Arrange
        var store = StoreErasing(new LocalMailFolderMailErasure(ErasedEmailCount: MaxEmailsPerPass, EmailsRemain: true));
        var jobs = Substitute.For<IJobStore>();
        var queued = new List<JobEnqueueRequest>();
        jobs.EnqueueAsync(Arg.Do<JobEnqueueRequest>(queued.Add), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(JobEnqueueResult.Created(JobId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000009")))));
        var handler = HandlerOver(store, jobs);

        // Act
        await handler.RunAsync(FirstPass, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(queued);
        var next = Assert.IsType<EraseLocalMailFolderMailJobPayload>(request.Payload);

        Assert.Equal(1, next.Pass);
        Assert.Equal(Account, request.Account);
    }

    [Fact]
    public async Task RunAsync_AQueueAtCapacity_FailsThePassSoItIsRetried()
    {
        // Arrange
        var store = StoreErasing(new LocalMailFolderMailErasure(ErasedEmailCount: MaxEmailsPerPass, EmailsRemain: true));
        var jobs = Substitute.For<IJobStore>();
        jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(JobEnqueueResult.RefusedAtCapacity()));
        var handler = HandlerOver(store, jobs);

        // Act and Assert
        await Assert.ThrowsAsync<JobHandOnRefusedAtCapacityException>(() =>
            handler.RunAsync(FirstPass, TestContext.Current.CancellationToken));
    }

    private static ILocalMailFolderStore StoreErasing(LocalMailFolderMailErasure erasure)
    {
        var store = Substitute.For<ILocalMailFolderStore>();
        store.EraseMailOfErasedFoldersAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(erasure);

        return store;
    }

    private static LocalMailFolderMailErasureHandler HandlerOver(ILocalMailFolderStore store, IJobStore jobs)
    {
        var clock = new FakeTimeProvider();
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new LocalMailFolderMailErasureHandler(
            store,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
            new MailboxSynchronizationOptions { MaxReconciledEmailsPerRun = MaxEmailsPerPass },
            jobs);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
