// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Chunking;

/// <summary>Covers the stage that cuts passages once classification and the rules have had their turn.</summary>
/// <remarks>
/// Which mail reaches the pass is the store's decision and is asserted where that predicate lives. What is asserted here
/// is the pass's own contract: every message is committed before it is offered, an offer the backlog refuses loses
/// nothing, the gate's answer is reported as the release it is, and a walk that is longer than one batch ends rather
/// than repeating itself.
/// </remarks>
public sealed class MailChunkingPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    [Fact]
    public async Task RunAsync_MailAwaitingTheCut_CutsEachMessageAndOffersItForEmbedding()
    {
        // Arrange
        var first = StoredEmailId.Create(Guid.CreateVersion7());
        var second = StoredEmailId.Create(Guid.CreateVersion7());
        var store = StoreReturning([Awaiting(first), Awaiting(second)]);
        var backlog = new RecordingEmailEmbeddingBacklog();
        var pass = CreatePass(store, backlog);

        // Act
        var report = await pass.RunAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(2, report.ChunkedEmailCount);
        Assert.Equal(0, report.RefusedOfferCount);
        Assert.False(report.EmailsRemain);
        Assert.Equal([first, second], backlog.Accepted);
        await store.Received(1).DeriveChunksAsync(Arg.Any<IPersistenceSession>(), first, CancellationToken.None);
        await store.Received(1).DeriveChunksAsync(Arg.Any<IPersistenceSession>(), second, CancellationToken.None);
    }

    /// <summary>The ordering the offer rests on: a message is durable before the worker is told about it.</summary>
    [Fact]
    public async Task RunAsync_OneMessage_CommitsItsPassagesBeforeOfferingIt()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var backlog = new RecordingEmailEmbeddingBacklog();
        var committed = false;
        var store = StoreReturning([Awaiting(storedEmailId)]);
        store
            .DeriveChunksAsync(Arg.Any<IPersistenceSession>(), storedEmailId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.Empty(backlog.Accepted);
                committed = true;

                return Task.CompletedTask;
            });

        // Act
        await CreatePass(store, backlog).RunAsync(Account, CancellationToken.None);

        // Assert
        Assert.True(committed);
        Assert.Equal([storedEmailId], backlog.Accepted);
    }

    /// <summary>A full backlog is the expected outcome of a first synchronization rather than a fault.</summary>
    [Fact]
    public async Task RunAsync_BacklogRefusesTheOffer_StillCountsTheMessageAsCut()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var backlog = new RecordingEmailEmbeddingBacklog { Capacity = 0 };
        var pass = CreatePass(StoreReturning([Awaiting(storedEmailId)]), backlog);

        // Act
        var report = await pass.RunAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(1, report.ChunkedEmailCount);
        Assert.Equal(1, report.RefusedOfferCount);
        Assert.Empty(backlog.Accepted);
        Assert.Equal(1, backlog.RefusedCount);
    }

    /// <summary>Cutting a message the gate was holding is the release, and the release is what an operator reads.</summary>
    [Fact]
    public async Task RunAsync_AMessageReleasedAfterWaiting_ReportsThatAdmission()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var telemetry = new RecordingDerivedWorkGateTelemetry();
        var pass = CreatePass(
            StoreReturning([new StoredEmailAwaitingChunking(storedEmailId, DerivedWorkAdmission.ReleasedAfterWaiting)]),
            new RecordingEmailEmbeddingBacklog(),
            telemetry);

        // Act
        await pass.RunAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal([DerivedWorkAdmission.ReleasedAfterWaiting], telemetry.Admissions);
    }

    [Fact]
    public async Task RunAsync_NothingAwaitingTheCut_ReportsAnEmptyPass()
    {
        // Arrange
        var store = StoreReturning([]);
        var pass = CreatePass(store, new RecordingEmailEmbeddingBacklog());

        // Act
        var report = await pass.RunAsync(Account, CancellationToken.None);

        // Assert
        Assert.True(report.IsEmpty);
        Assert.False(report.EmailsRemain);
        await store.DidNotReceiveWithAnyArgs().DeriveChunksAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<StoredEmailId>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A pass that keeps filling its batch stops at its own bound and says so, rather than holding the account's run
    /// open for a mailbox whose whole content is awaiting the cut.
    /// </summary>
    [Fact]
    public async Task RunAsync_EveryBatchFull_EndsOnItsBoundAndReportsMailRemaining()
    {
        // Arrange
        var store = Substitute.For<IStoredEmailChunkingStore>();
        store
            .GetEmailsAwaitingChunkingAsync(Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<StoredEmailAwaitingChunking>>(
                [.. Enumerable
                    .Range(0, call.ArgAt<int>(1))
                    .Select(_ => Awaiting(StoredEmailId.Create(Guid.CreateVersion7())))]));
        var pass = CreatePass(store, new RecordingEmailEmbeddingBacklog());

        // Act
        var report = await pass.RunAsync(Account, CancellationToken.None);

        // Assert
        Assert.True(report.EmailsRemain);
        Assert.True(report.ChunkedEmailCount > 0);
        await store.Received(25).GetEmailsAwaitingChunkingAsync(
            Account,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    private static StoredEmailAwaitingChunking Awaiting(StoredEmailId storedEmailId) => new(storedEmailId);

    private static IStoredEmailChunkingStore StoreReturning(IReadOnlyList<StoredEmailAwaitingChunking> batch)
    {
        var store = Substitute.For<IStoredEmailChunkingStore>();

        // The second answer is empty because cutting is what takes a message out of the query the pass re-issues, so a
        // store that kept returning the same batch would describe a defect rather than the walk.
        store
            .GetEmailsAwaitingChunkingAsync(Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(batch, []);

        return store;
    }

    private static MailChunkingPass CreatePass(
        IStoredEmailChunkingStore store,
        RecordingEmailEmbeddingBacklog backlog,
        RecordingDerivedWorkGateTelemetry? telemetry = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new MailChunkingPass(
            store,
            backlog,
            telemetry ?? new RecordingDerivedWorkGateTelemetry(),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero))));
    }
}
