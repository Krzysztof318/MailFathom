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
/// Every case states a bound of nothing, so the first answer each question gets is the answer: what is being asserted
/// is which answer refuses and which lets the work through, never how long the wait before it lasted. A bound a test
/// waited out would prove the same thing several seconds later and make the suite's own clock part of the claim.
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

        // Act
        var refusal = await quiescing.RunQuiescedAsync([], _ => Task.CompletedTask, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        await leases.DidNotReceive()
            .ClaimAsync(Arg.Any<WorkScope>(), Arg.Any<WorkLeaseHolder>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    private static MailAccountWorkQuiesce Quiescing(IServiceProvider services, FakeTimeProvider clock) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new StubSettingsSnapshot<MailSynchronizationOptions>(new MailSynchronizationOptions()),
            NullLoggerFactory.Instance,
            clock,
            TimeSpan.Zero);

    private static ServiceProvider Composed(IWorkLeaseStore leases, IJobStore jobs) => new ServiceCollection()
        .AddSingleton(leases)
        .AddSingleton(jobs)
        .BuildServiceProvider();

    private static IJobStore JobsInFlightFor(params MailAccountId[] accounts)
    {
        var jobs = Substitute.For<IJobStore>();
        jobs.ReadAccountsWithWorkInFlightAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([.. accounts.Select(static account => account.Value)]);

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
