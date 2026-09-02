// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Resilience;

/// <summary>
/// Covers the virtual-time pump every resilience and mail-session test drives its pipelines with. A test that asserts
/// which of two nested budgets expired is only evidence about the product while the pump holds the clock for the work
/// each step starts, so what the pump guarantees is worth proving here rather than in each of its callers.
/// </summary>
public sealed class OutboundResilienceTestHostTests
{
    /// <summary>
    /// Enough thread-pool hops that a loop trading a fixed number of scheduler turns for an advance would move the
    /// clock several times over before the work of the first one surfaced.
    /// </summary>
    private const int HopsBeforeTheFailureSurfaces = 512;

    /// <summary>The budget that comes due, chosen so a step of the same length brings it due on the first advance.</summary>
    private static readonly TimeSpan ExpiringBudget = TimeSpan.FromSeconds(1);

    /// <summary>A budget that comes due must be what a test reads, and it stops being that once a later one can expire first.</summary>
    [Fact]
    public async Task CompleteOnVirtualTimeAsync_WorkSurfacingSeveralHopsLater_HoldsTheClockAtTheStepThatStartedIt()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var startedAt = host.TimeProvider.GetUtcNow();
        using var budget = new CancellationTokenSource(ExpiringBudget, host.TimeProvider);
        var observedAt = default(DateTimeOffset);

        // Act
        var execution = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, budget.Token);
                }
                catch (OperationCanceledException)
                {
                    for (var hop = 0; hop < HopsBeforeTheFailureSurfaces; hop++)
                    {
                        await Task.Yield();
                    }

                    observedAt = host.TimeProvider.GetUtcNow();
                }
            },
            TestContext.Current.CancellationToken);

        await host.CompleteOnVirtualTimeAsync(execution, ExpiringBudget);

        // Assert
        Assert.Equal(startedAt + ExpiringBudget, observedAt);
    }

    /// <summary>An execution that stops answering is a defect to read, so the pump ends it rather than the suite's timeout.</summary>
    [Fact]
    public async Task CompleteOnVirtualTimeAsync_ExecutionThatNeverCompletes_FailsNamingTheAdvancesItSpent()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var stalled = new TaskCompletionSource();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.CompleteOnVirtualTimeAsync(stalled.Task, ExpiringBudget, maximumAdvances: 4));

        // Assert
        Assert.Contains("4 advances", failure.Message, StringComparison.Ordinal);
    }
}
