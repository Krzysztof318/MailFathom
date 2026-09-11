// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Host.Hosting.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class WorkLeaseHoldTests
{
    /// <summary>Guards against a hung hold. No assertion depends on how long anything actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(30);

    /// <summary>A claim the database failed to answer is not a claim granted, so the work stays unheld here.</summary>
    [Fact]
    public async Task TryTakeAsync_ClaimThrows_TakesNothing()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var store = Substitute.For<IWorkLeaseStore>();
        store.ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WorkLease?>(new InvalidOperationException("The database is unreachable.")));
        await using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();

        // Act
        using var hold = await TakeAsync(services, clock);

        // Assert
        Assert.Null(hold);
    }

    /// <summary>A renewal the database failed is one this replica can no longer show it holds, so the work it guards stops.</summary>
    [Fact]
    public async Task KeepAsync_RenewalThrows_LosesTheHold()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var store = StoreGrantingClaims(clock);
        store.RenewAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WorkLease?>(new InvalidOperationException("The database is unreachable.")));
        await using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        using var hold = await TakeAsync(services, clock);
        using var renewalStop = new CancellationTokenSource();
        var renewals = hold!.KeepAsync(renewalStop.Token);

        // Act
        clock.Advance(RenewalInterval);
        await renewals.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(hold.Lost.IsCancellationRequested);
    }

    /// <summary>
    /// A database that stops answering is the case a refused renewal does not cover: nothing ever says the hold moved,
    /// so the hold has to give up on the answer while there is still margin left for the work it guards to stop in.
    /// </summary>
    [Fact]
    public async Task KeepAsync_RenewalNeverAnswered_LosesTheHoldBeforeTheLeaseCouldExpire()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var renewalSent = new TaskCompletionSource();
        var store = StoreGrantingClaims(clock);
        store.RenewAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => NeverAnswerAsync(renewalSent, call.Arg<CancellationToken>()));
        await using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        var claimedAt = clock.GetUtcNow();
        using var hold = await TakeAsync(services, clock);
        using var renewalStop = new CancellationTokenSource();
        var renewals = hold!.KeepAsync(renewalStop.Token);

        // Act
        clock.Advance(RenewalInterval);
        await renewalSent.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        clock.Advance((LeaseDuration - RenewalInterval) / 2);
        await renewals.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(hold.Lost.IsCancellationRequested);
        Assert.True(clock.GetUtcNow() - claimedAt < LeaseDuration);
    }

    /// <summary>Work run under a hold gives the scope back once it ends, so the next piece of work is contested afresh.</summary>
    [Fact]
    public async Task RunWhileHeldAsync_WorkEnds_GivesTheScopeBackAndAnswersWithWhatTheWorkProduced()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var store = StoreGrantingClaims(clock);
        await using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        using var hold = await TakeAsync(services, clock);

        // Act
        var produced = await hold!.RunWhileHeldAsync(_ => Task.FromResult(7), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, produced);
        await store.Received(1).ReleaseAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A hold lost while its work runs stops that work, rather than letting it run on and write as a second replica's.</summary>
    [Fact]
    public async Task RunWhileHeldAsync_HoldLostWhileTheWorkRuns_CancelsTheWorkAndStillGivesTheScopeBack()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var workStarted = new TaskCompletionSource();
        var store = StoreGrantingClaims(clock);
        store.RenewAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkLease?>(null));
        await using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        using var hold = await TakeAsync(services, clock);

        // Act
        var running = hold!.RunWhileHeldAsync(
            token => RunUntilCancelledAsync(workStarted, token),
            TestContext.Current.CancellationToken);
        await workStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        clock.Advance(RenewalInterval);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken));
        Assert.True(hold.Lost.IsCancellationRequested);
        await store.Received(1).ReleaseAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Models guarded work that runs until it is told to stop.</summary>
    private static async Task<int> RunUntilCancelledAsync(TaskCompletionSource started, CancellationToken cancellationToken)
    {
        started.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return 0;
    }

    private static IWorkLeaseStore StoreGrantingClaims(FakeTimeProvider clock)
    {
        var store = Substitute.For<IWorkLeaseStore>();
        store.ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<WorkLease?>(
                new WorkLease(call.Arg<WorkScope>()!, call.Arg<WorkLeaseHolder>()!, clock.GetUtcNow() + LeaseDuration)));

        return store;
    }

    private static Task<WorkLeaseHold?> TakeAsync(ServiceProvider services, FakeTimeProvider clock) => WorkLeaseHold.TryTakeAsync(
        WorkScope.Create("work-lease-hold-test"),
        LeaseDuration,
        RenewalInterval,
        services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<WorkLeaseHold>.Instance,
        clock,
        TestContext.Current.CancellationToken);

    /// <summary>Models a database that accepts the statement and then answers nothing until the caller gives up.</summary>
    private static async Task<WorkLease?> NeverAnswerAsync(TaskCompletionSource sent, CancellationToken cancellationToken)
    {
        sent.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return null;
    }
}
