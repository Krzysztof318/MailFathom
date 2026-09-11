// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class WorkLeaseRunnerTests
{
    /// <summary>Guards against a hung hold. No assertion depends on how long anything actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(30);

    private static readonly WorkScope Scope = WorkScope.Create("work-lease-runner-test");

    /// <summary>A scope another replica holds is not this one's to work, so the work never starts.</summary>
    [Fact]
    public async Task TryRunUnderLeaseAsync_ScopeHeldElsewhere_RunsNothing()
    {
        // Arrange
        var store = new ScriptedWorkLeaseStore { HeldElsewhere = true };
        await using var services = ServicesOver(store);
        var ran = false;

        // Act
        var held = await RunnerOver(services, new FakeTimeProvider()).TryRunUnderLeaseAsync(
            Scope,
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((false, false), (held, ran));
    }

    /// <summary>Work runs while the scope is held and gives it back once it ends, so a successor finds it free.</summary>
    [Fact]
    public async Task TryRunUnderLeaseAsync_WorkThatEnds_IsHeldWhileItRunsAndGivesTheScopeBack()
    {
        // Arrange
        var store = new ScriptedWorkLeaseStore();
        await using var services = ServicesOver(store);
        IReadOnlyCollection<WorkScope> heldWhileWorking = [];

        // Act
        var held = await RunnerOver(services, new FakeTimeProvider()).TryRunUnderLeaseAsync(
            Scope,
            _ =>
            {
                heldWhileWorking = store.HeldScopes;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(held);
        Assert.Equal([Scope], heldWhileWorking);
        Assert.Equal([Scope], store.Releases);
        Assert.Empty(store.HeldScopes);
    }

    /// <summary>A renewal another replica refused stops the work at once rather than when its next pass would have looked.</summary>
    [Fact]
    public async Task TryRunUnderLeaseAsync_RenewalRefused_CancelsTheWorkItGuards()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var store = new ScriptedWorkLeaseStore();
        await using var services = ServicesOver(store);
        var working = new TaskCompletionSource();
        var stopped = false;

        var running = RunnerOver(services, clock).TryRunUnderLeaseAsync(
            Scope,
            async token =>
            {
                working.SetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    stopped = true;
                }
            },
            TestContext.Current.CancellationToken);

        await working.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Act
        store.HeldElsewhere = true;
        clock.Advance(RenewalInterval);
        var held = await running.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((true, true), (held, stopped));
    }

    private static ServiceProvider ServicesOver(ScriptedWorkLeaseStore store) =>
        new ServiceCollection().AddSingleton<IWorkLeaseStore>(store).BuildServiceProvider();

    private static WorkLeaseRunner RunnerOver(ServiceProvider services, FakeTimeProvider clock) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        NullLoggerFactory.Instance,
        clock,
        LeaseDuration,
        RenewalInterval);
}
