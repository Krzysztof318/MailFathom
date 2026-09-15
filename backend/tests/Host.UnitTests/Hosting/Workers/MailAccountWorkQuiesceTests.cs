// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Jobs;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Proves what an erasure is allowed to run over, and what it is refused for.</summary>
/// <remarks>
/// Most cases state a bound of nothing, so the first answer each question gets is the answer: what is being asserted
/// is which answer refuses and which lets the work through, never how long the wait before it lasted. A bound a test
/// waited out would prove the same thing several seconds later and make the suite's own clock part of the claim. The
/// one case that states a bound is the one whose claim is that a refused account is asked about again at all, which a
/// zero bound cannot reach; it advances a <see cref="FakeTimeProvider" /> rather than waiting, so it costs the same.
/// </remarks>
public sealed class MailAccountWorkQuiesceTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("91d6f2c8-0b7a-4e52-8c31-5a7d9e4b6102");

    private static readonly MailAccountId SecondAccount =
        MailAccountId.Create("a2e7038d-1c8b-4f63-9d42-6b8e0f5c7213");

    [Fact]
    public async Task RunQuiescedAsync_AccountsNothingIsWriting_RunsTheWorkAndGivesEveryHoldBack()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var leases = StoreGrantingClaims(clock);
        await using var services = Composed(leases, JobsInFlightFor());
        var quiescing = Quiescing(services, clock);
        var ran = false;

        // Act
        var refusal = await quiescing.RunQuiescedAsync(
            [Account, SecondAccount],
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        Assert.True(ran);

        // Both scopes are given back, because an account left held is one nothing supervises until its lease expires.
        await leases.Received(2)
            .ReleaseAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The scope is what synchronization runs under, so an account held elsewhere is one still being written to.</summary>
    [Fact]
    public async Task RunQuiescedAsync_AnAccountSupervisedElsewhere_RefusesWithoutRunningTheWork()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var leases = Substitute.For<IWorkLeaseStore>();
        leases.ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((WorkLease?)null);
        await using var services = Composed(leases, JobsInFlightFor());
        var quiescing = Quiescing(services, clock);
        var ran = false;

        // Act
        var refusal = await quiescing.RunQuiescedAsync(
            [Account],
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(ran);
        Assert.NotNull(refusal);
        Assert.Contains(Account.Value, refusal, StringComparison.Ordinal);
    }

    /// <summary>A handler holding a job writes rows keyed to the account, which no deletion inside one transaction reaches.</summary>
    [Fact]
    public async Task RunQuiescedAsync_AJobStillHeldForAnAccount_RefusesWithoutRunningTheWork()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var leases = StoreGrantingClaims(clock);
        await using var services = Composed(leases, JobsInFlightFor(SecondAccount));
        var quiescing = Quiescing(services, clock);
        var ran = false;

        // Act
        var refusal = await quiescing.RunQuiescedAsync(
            [Account, SecondAccount],
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(ran);
        Assert.NotNull(refusal);
        Assert.Contains(SecondAccount.Value, refusal, StringComparison.Ordinal);

        // The supervision taken before the job was read is given back, so a refusal leaves no account unsupervised.
        await leases.Received(2)
            .ReleaseAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A user assigned no mailbox of their own has nothing to stop, and is not made to wait for one.</summary>
    [Fact]
    public async Task RunQuiescedAsync_NoAccountAtAll_RunsTheWorkAndHoldsNothing()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var leases = StoreGrantingClaims(clock);
        await using var services = Composed(leases, JobsInFlightFor());
        var quiescing = Quiescing(services, clock);
        var ran = false;

        // Act
        var refusal = await quiescing.RunQuiescedAsync(
            [],
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        Assert.True(ran);
        await leases.DidNotReceive()
            .ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The whole design rests on this: a hold that stops being provable has to take the deletion with it.</summary>
    [Fact]
    public async Task RunQuiescedAsync_AHoldLostWhileTheWorkRuns_CancelsTheWorkAndRefusesNamingTheAccount()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var leases = StoreGrantingClaims(clock);
        leases.RenewAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((WorkLease?)null);
        await using var services = Composed(leases, JobsInFlightFor());
        var quiescing = Quiescing(services, clock);
        var started = new TaskCompletionSource();
        var handedToTheWork = CancellationToken.None;

        // Act
        var run = quiescing.RunQuiescedAsync(
            [Account],
            async token =>
            {
                handedToTheWork = token;
                started.SetResult();

                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            TestContext.Current.CancellationToken);

        await started.Task;

        // The renewal is due once the interval has passed, and the store above refuses it.
        clock.Advance(new MailSynchronizationOptions().LeaseRenewalInterval + TimeSpan.FromSeconds(1));

        var refusal = await run;

        // Assert
        Assert.True(handedToTheWork.IsCancellationRequested);
        Assert.NotNull(refusal);
        Assert.Contains(Account.Value, refusal, StringComparison.Ordinal);
    }

    /// <summary>An account another replica is draining is the ordinary case, so asking again is the wait itself.</summary>
    [Fact]
    public async Task RunQuiescedAsync_AnAccountFreedAfterTheFirstRefusal_AsksAgainAndRunsTheWork()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var claims = 0;
        var leases = Substitute.For<IWorkLeaseStore>();
        leases.ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => Interlocked.Increment(ref claims) == 1
                ? Task.FromResult<WorkLease?>(null)
                : Task.FromResult<WorkLease?>(
                    new WorkLease(
                        call.Arg<WorkScope>()!,
                        call.Arg<WorkLeaseHolder>()!,
                        ReplicaIdentity.Create("mail-account-work-quiesce-test"),
                        clock.GetUtcNow() + TimeSpan.FromMinutes(2))));
        await using var services = Composed(leases, JobsInFlightFor());
        var quiescing = Quiescing(services, clock, TimeSpan.FromMinutes(5));
        var ran = false;

        // Act
        var run = quiescing.RunQuiescedAsync(
            [Account],
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Each advance is a second of the bound's five minutes, so the run ends on the second claim rather than on the
        // bound. The loop is what makes the delay's registration and the advance independent of the scheduler.
        for (var tick = 0; tick < 20 && !run.IsCompleted; tick++)
        {
            await Task.Yield();

            clock.Advance(TimeSpan.FromSeconds(1));
        }

        var refusal = await run;

        // Assert
        Assert.Null(refusal);
        Assert.True(ran);

        // The first claim was refused, so the account was asked about a second time rather than given up on.
        await leases.Received(2)
            .ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    private static MailAccountWorkQuiesce Quiescing(
        IServiceProvider services,
        FakeTimeProvider clock,
        TimeSpan? bound = null) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new StubSettingsSnapshot<MailSynchronizationOptions>(new MailSynchronizationOptions()),
            NullLoggerFactory.Instance,
            clock,
            bound ?? TimeSpan.Zero);

    private static ServiceProvider Composed(IWorkLeaseStore leases, IJobStore jobs) => new ServiceCollection()
        .AddSingleton(leases)
        .AddSingleton(jobs)
        .BuildServiceProvider();

    private static IJobStore JobsInFlightFor(params MailAccountId[] accounts)
    {
        var jobs = Substitute.For<IJobStore>();
        jobs.ReadAccountsWithWorkInFlightAsync(Arg.Any<IReadOnlyList<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns([.. accounts]);

        return jobs;
    }

    private static IWorkLeaseStore StoreGrantingClaims(FakeTimeProvider clock)
    {
        var leases = Substitute.For<IWorkLeaseStore>();
        leases.ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<WorkLease?>(
                new WorkLease(
                    call.Arg<WorkScope>()!,
                    call.Arg<WorkLeaseHolder>()!,
                    ReplicaIdentity.Create("mail-account-work-quiesce-test"),
                    clock.GetUtcNow() + TimeSpan.FromMinutes(2))));

        return leases;
    }
}
