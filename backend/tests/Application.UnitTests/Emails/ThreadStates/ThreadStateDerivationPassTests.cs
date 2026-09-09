// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ThreadStates;

/// <summary>Covers the pass that writes down where an account's conversations stand.</summary>
/// <remarks>
/// Which conversations reach the pass is the store's predicate and is asserted where that predicate lives. What is
/// asserted here is the pass's own contract — that a settled derivation is committed whether or not it found anything,
/// that a conversation past the bound is recorded rather than left to be asked again forever, that a withheld one
/// writes nothing and ends the pass, and that a full batch is reported as work remaining.
/// </remarks>
public sealed class ThreadStateDerivationPassTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("work"));

    private static readonly DateTimeOffset DerivedAt = new(2026, 9, 8, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ConversationsAwaitingAState_WritesOneRecordEach()
    {
        // Arrange
        var first = Derivable();
        var second = Derivable();
        var store = StoreReturning([first, second]);
        var pass = CreatePass(store, DeriverAnswering(_ => ThreadStateDerivation.Settled([Agreement()])));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.DerivedThreadCount);
        Assert.Equal(2, report.StatedThreadCount);
        Assert.Equal(0, report.TooLargeThreadCount);
        Assert.Null(report.StoppedBy);
        Assert.False(report.ThreadsRemain);
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailThreadState>(state =>
                state!.ThreadId == first.ThreadId && state.DerivedAt == DerivedAt),
            Arg.Any<CancellationToken>());
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailThreadState>(state => state!.ThreadId == second.ThreadId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The shape the conversation had when it was read is what the record is stored against, which is the whole of how
    /// a state is kept current: a conversation that gains a reply no longer matches and is derived again.
    /// </summary>
    [Fact]
    public async Task RunAsync_ADerivedConversation_RecordsTheShapeItWasDerivedFrom()
    {
        // Arrange
        var thread = Derivable();
        var store = StoreReturning([thread]);
        var pass = CreatePass(store, DeriverAnswering(_ => ThreadStateDerivation.Settled([])));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailThreadState>(state => state!.DerivedFrom == thread.Revision),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A conversation there is nothing to say about is settled, so it never returns to the queue.</summary>
    [Fact]
    public async Task RunAsync_ADerivationThatFoundNothingToSay_StillWritesTheRecord()
    {
        // Arrange
        var store = StoreReturning([Derivable()]);
        var pass = CreatePass(store, DeriverAnswering(_ => ThreadStateDerivation.Settled([])));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.DerivedThreadCount);
        Assert.Equal(0, report.StatedThreadCount);
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailThreadState>(state => state!.Entries.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A conversation past the bound is a settled record rather than a withholding, because nothing about it changes
    /// next run: recorded as too large it leaves the queue, and withheld it would stand at the head of every pass for
    /// the life of the mailbox.
    /// </summary>
    [Fact]
    public async Task RunAsync_AConversationPastTheBound_RecordsItAsTooLargeWithNoStatements()
    {
        // Arrange
        var store = StoreReturning([Derivable(), Derivable()]);
        var pass = CreatePass(store, DeriverAnswering(_ => ThreadStateDerivation.TooLarge()));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.DerivedThreadCount);
        Assert.Equal(2, report.TooLargeThreadCount);
        Assert.Equal(0, report.StatedThreadCount);
        Assert.Null(report.StoppedBy);
        await store.Received(2).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailThreadState>(state =>
                state!.Coverage == ThreadStateCoverage.ThreadTooLarge && state.Entries.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Every reason a derivation is withheld outlives one conversation, so the pass stops on the first rather than
    /// buying the same answer for each conversation behind it.
    /// </summary>
    [Theory]
    [InlineData(ThreadStateWithholding.NotActivated)]
    [InlineData(ThreadStateWithholding.AllowanceExhausted)]
    [InlineData(ThreadStateWithholding.ProviderUnavailable)]
    public async Task RunAsync_ADerivationWithheld_WritesNothingAndEndsThePass(ThreadStateWithholding withholding)
    {
        // Arrange
        var store = StoreReturning([Derivable(), Derivable()]);
        var deriver = DeriverAnswering(_ => ThreadStateDerivation.Withholding(withholding));
        var pass = CreatePass(store, deriver);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(withholding, report.StoppedBy);
        Assert.Equal(0, report.DerivedThreadCount);
        Assert.True(report.ThreadsRemain);
        await deriver.Received(1).DeriveAsync(Arg.Any<DerivableThread>(), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<EmailThreadState>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>What one conversation's derivation settled is durable before the next one costs a provider call.</summary>
    [Fact]
    public async Task RunAsync_ThePassStoppingHalfWayThrough_KeepsWhatEarlierConversationsSettled()
    {
        // Arrange
        var first = Derivable();
        var second = Derivable();
        var store = StoreReturning([first, second]);
        var pass = CreatePass(
            store,
            DeriverAnswering(thread => thread.ThreadId == first.ThreadId
                ? ThreadStateDerivation.Settled([Agreement()])
                : ThreadStateDerivation.Withholding(ThreadStateWithholding.ProviderUnavailable)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.DerivedThreadCount);
        Assert.Equal(ThreadStateWithholding.ProviderUnavailable, report.StoppedBy);
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailThreadState>(state => state!.ThreadId == first.ThreadId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NothingAwaitingAState_ReportsAnEmptyPass()
    {
        // Arrange
        var deriver = DeriverAnswering(_ => ThreadStateDerivation.Settled([]));
        var pass = CreatePass(StoreReturning([]), deriver);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.IsEmpty);
        Assert.False(report.ThreadsRemain);
        await deriver.DidNotReceiveWithAnyArgs().DeriveAsync(
            Arg.Any<DerivableThread>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mailbox turned on for the first time is drained over successive runs rather than in one pass, which is what
    /// makes the backfill bounded.
    /// </summary>
    [Fact]
    public async Task RunAsync_AFullBatch_EndsOnItsBoundAndReportsConversationsRemaining()
    {
        // Arrange
        var store = StoreReturning(
            [.. Enumerable.Range(0, ThreadStateDerivationPass.MaximumThreadsPerPass).Select(_ => Derivable())]);
        var pass = CreatePass(store, DeriverAnswering(_ => ThreadStateDerivation.Settled([])));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ThreadStateDerivationPass.MaximumThreadsPerPass, report.DerivedThreadCount);
        Assert.True(report.ThreadsRemain);
        await store.Received(1).GetThreadsAwaitingStateAsync(
            Account,
            ThreadStateDerivationPass.MaximumThreadsPerPass,
            ThreadStateDerivationPass.MaximumMessagesPerThread,
            ThreadStateDerivationPass.MaximumCharactersPerMessage,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The default deployment, where the switch is answered before anything is read. A pass that queried first would
    /// scan for work nothing was going to do, once per account per run, for the life of every instance that never
    /// turned the derivation on.
    /// </summary>
    [Fact]
    public async Task RunAsync_ADeploymentThatDerivesNothing_IssuesNoQueryAndReportsNothing()
    {
        // Arrange
        var store = StoreReturning([Derivable()]);
        var deriver = Substitute.For<IThreadStateDeriver>();
        deriver.IsActive.Returns(false);
        var pass = CreatePass(store, deriver);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.IsEmpty);
        Assert.Equal(ThreadStateWithholding.NotActivated, report.StoppedBy);
        Assert.False(report.ThreadsRemain);
        await store.DidNotReceiveWithAnyArgs().GetThreadsAwaitingStateAsync(
            Arg.Any<MailAccountIdentity>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A condition that stopped a working pass is worth a line, unlike the switch that was never turned on.</summary>
    [Theory]
    [InlineData(ThreadStateWithholding.AllowanceExhausted)]
    [InlineData(ThreadStateWithholding.ProviderUnavailable)]
    public async Task RunAsync_APassAWithholdingStopped_IsReportedRatherThanPassedOver(
        ThreadStateWithholding withholding)
    {
        // Arrange
        var deriver = DeriverAnswering(_ => ThreadStateDerivation.Withholding(withholding));
        var pass = CreatePass(StoreReturning([Derivable()]), deriver);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(report.IsEmpty);
        Assert.Equal(withholding, report.StoppedBy);
    }

    private static DerivableThread Derivable() =>
        new(
            EmailThreadId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new ThreadStateRevision(2, new DateTimeOffset(2026, 9, 7, 16, 0, 0, TimeSpan.Zero)),
            [
                new DerivableThreadMessage(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    0,
                    "Karolina",
                    new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.Zero),
                    "we can hold the two-hour response time"),
            ],
            ExceedsBound: false);

    private static ThreadStateEntry Agreement() =>
        ThreadStateEntry.Create(
            ThreadStateAspect.Agreement,
            "The response time stays at two hours.",
            [StoredEmailId.Create(Guid.CreateVersion7())]);

    private static IStoredThreadStateStore StoreReturning(IReadOnlyList<DerivableThread> batch)
    {
        var store = Substitute.For<IStoredThreadStateStore>();
        store
            .GetThreadsAwaitingStateAsync(
                Account,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(batch);

        return store;
    }

    private static IThreadStateDeriver DeriverAnswering(Func<DerivableThread, ThreadStateDerivation> answer)
    {
        var deriver = Substitute.For<IThreadStateDeriver>();
        deriver.IsActive.Returns(true);
        deriver
            .DeriveAsync(Arg.Any<DerivableThread>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(answer(call.ArgAt<DerivableThread>(0))));

        return deriver;
    }

    /// <summary>Composes the pass over a deployment with no scanner switched on, which is the ordinary shape.</summary>
    private static ThreadStateDerivationPass CreatePass(
        IStoredThreadStateStore store,
        IThreadStateDeriver deriver)
    {
        var timeProvider = new FakeTimeProvider(DerivedAt);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new ThreadStateDerivationPass(
            store,
            deriver,
            SensitiveContentEgressGuards.Inactive(),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                timeProvider),
            timeProvider);
    }
}
