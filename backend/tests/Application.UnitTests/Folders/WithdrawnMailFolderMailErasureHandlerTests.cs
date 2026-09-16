// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders;

/// <summary>Covers the bounded passes that erase the stored mail of a folder a mirrored account no longer declares.</summary>
public sealed class WithdrawnMailFolderMailErasureHandlerTests
{
    private const int MaxEmailsPerPass = 500;

    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly EraseWithdrawnMailFolderMailJobPayload FirstPass =
        EraseWithdrawnMailFolderMailJobPayload.For(Account, MailFolderAlias.Create("projects"));

    [Fact]
    public async Task RunAsync_APassThatLeavesNothing_ErasesUnderTheBoundAndQueuesNoOther()
    {
        // Arrange
        var store = StoreErasing(new MailFolderMirrorErasure(ErasedEmailCount: 3, EmailsRemain: false));
        var jobs = Substitute.For<IJobStore>();
        var handler = HandlerOver(store, jobs);

        // Act
        await handler.RunAsync(FirstPass, TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).EraseFolderMirrorAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            MailFolderAlias.Create("projects"),
            true,
            MaxEmailsPerPass,
            Arg.Any<CancellationToken>());
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_APassThatLeavesMail_QueuesTheNextPassOverTheSameFolder()
    {
        // Arrange
        var store = StoreErasing(new MailFolderMirrorErasure(ErasedEmailCount: MaxEmailsPerPass, EmailsRemain: true));
        var jobs = Substitute.For<IJobStore>();
        var queued = new List<JobEnqueueRequest>();
        jobs.EnqueueAsync(Arg.Do<JobEnqueueRequest>(queued.Add), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(JobEnqueueResult.Created(JobId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000009")))));
        var handler = HandlerOver(store, jobs);

        // Act
        await handler.RunAsync(FirstPass, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(queued);
        var next = Assert.IsType<EraseWithdrawnMailFolderMailJobPayload>(request.Payload);

        Assert.Equal(1, next.Pass);
        Assert.Equal(MailFolderAlias.Create("projects"), next.Folder);
        Assert.Equal(Account, request.Account);
    }

    [Fact]
    public async Task RunAsync_AQueueAtCapacity_FailsThePassSoItIsRetried()
    {
        // Arrange
        var store = StoreErasing(new MailFolderMirrorErasure(ErasedEmailCount: MaxEmailsPerPass, EmailsRemain: true));
        var jobs = Substitute.For<IJobStore>();
        jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(JobEnqueueResult.RefusedAtCapacity()));
        var handler = HandlerOver(store, jobs);

        // Act, Assert
        await Assert.ThrowsAsync<JobHandOnRefusedAtCapacityException>(() =>
            handler.RunAsync(FirstPass, TestContext.Current.CancellationToken));
    }

    /// <summary>A payload of another job type reaching this handler is a wiring fault rather than mail to erase.</summary>
    [Fact]
    public async Task RunAsync_APayloadOfAnotherJobType_IsRefused()
    {
        // Arrange
        var handler = HandlerOver(StoreErasing(MailFolderMirrorErasure.Nothing), Substitute.For<IJobStore>());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.RunAsync(Substitute.For<IJobPayload>(), TestContext.Current.CancellationToken));
    }

    private static IStoredMailFolderMirrorStore StoreErasing(MailFolderMirrorErasure erasure)
    {
        var store = Substitute.For<IStoredMailFolderMirrorStore>();
        store.EraseFolderMirrorAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderAlias>(),
                Arg.Any<bool>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(erasure);

        return store;
    }

    private static WithdrawnMailFolderMailErasureHandler HandlerOver(IStoredMailFolderMirrorStore store, IJobStore jobs)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new WithdrawnMailFolderMailErasureHandler(
            store,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), new FakeTimeProvider()),
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
