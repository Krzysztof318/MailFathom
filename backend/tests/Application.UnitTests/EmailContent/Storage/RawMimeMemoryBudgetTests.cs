// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage;

public sealed class RawMimeMemoryBudgetTests
{
    [Fact]
    public async Task ReserveAsync_WithinCapacity_TakesTheBytesAndReturnsThemOnDisposal()
    {
        // Arrange
        var budget = new RawMimeMemoryBudget(1000);

        // Act
        using (var reservation = await budget.ReserveAsync(400, CancellationToken.None))
        {
            // Assert
            Assert.Equal(400, reservation.Bytes);
            Assert.Equal(600, budget.AvailableBytes);
        }

        Assert.Equal(1000, budget.AvailableBytes);
    }

    /// <summary>A work unit whose share does not fit waits for one that finishes, rather than being refused.</summary>
    /// <remarks>
    /// This is the whole mechanism the budget exists for: peak memory stays at the capacity however many work units the
    /// concurrency bounds put in flight, and the excess ones are slowed instead of failing.
    /// </remarks>
    [Fact]
    public async Task ReserveAsync_BudgetIsHeld_WaitsUntilAHeldReservationIsReleased()
    {
        // Arrange
        var budget = new RawMimeMemoryBudget(1000);
        var held = await budget.ReserveAsync(700, CancellationToken.None);

        // Act
        var waiting = budget.ReserveAsync(700, CancellationToken.None);
        var waitedRatherThanFailing = !waiting.IsCompleted;
        held.Dispose();
        using var granted = await waiting;

        // Assert
        Assert.True(waitedRatherThanFailing);
        Assert.Equal(700, granted.Bytes);
        Assert.Equal(300, budget.AvailableBytes);
    }

    /// <summary>A large request is not starved by the small ones that arrive behind it.</summary>
    /// <remarks>
    /// Grants follow request order, so a request that does not fit blocks the queue behind it instead of letting a
    /// stream of smaller ones keep the budget permanently just short of what it needs.
    /// </remarks>
    [Fact]
    public async Task ReserveAsync_SmallRequestBehindALargeOne_IsGrantedOnlyAfterTheLargeOne()
    {
        // Arrange
        var budget = new RawMimeMemoryBudget(1000);
        var held = await budget.ReserveAsync(1000, CancellationToken.None);

        // Act
        var large = budget.ReserveAsync(1000, CancellationToken.None);
        var small = budget.ReserveAsync(1, CancellationToken.None);
        held.Dispose();
        using var grantedLarge = await large;

        // Assert
        Assert.Equal(1000, grantedLarge.Bytes);
        Assert.False(small.IsCompleted);

        grantedLarge.Dispose();
        using var grantedSmall = await small;
        Assert.Equal(1, grantedSmall.Bytes);
    }

    /// <summary>A request larger than the whole budget is refused, because no release could ever satisfy it.</summary>
    [Fact]
    public async Task ReserveAsync_RequestLargerThanTheCapacity_ThrowsRatherThanWaitingForever()
    {
        // Arrange
        var budget = new RawMimeMemoryBudget(1000);

        // Act
        var reserve = async () => await budget.ReserveAsync(1001, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(reserve);
    }

    /// <summary>A cancelled wait leaves no bytes reserved for a caller that walked away.</summary>
    [Fact]
    public async Task ReserveAsync_CancelledWhileWaiting_ReleasesNothingAndLeavesTheBudgetIntact()
    {
        // Arrange
        var budget = new RawMimeMemoryBudget(1000);
        using var cancellation = new CancellationTokenSource();
        var held = await budget.ReserveAsync(1000, CancellationToken.None);
        var waiting = budget.ReserveAsync(500, cancellation.Token);

        // Act
        await cancellation.CancelAsync();
        var cancelled = async () => await waiting;

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(cancelled);
        held.Dispose();
        Assert.Equal(1000, budget.AvailableBytes);
    }

    /// <summary>Disposing a reservation twice returns its bytes once, so a double release cannot enlarge the budget.</summary>
    [Fact]
    public async Task Dispose_CalledTwice_ReturnsTheBytesOnce()
    {
        // Arrange
        var budget = new RawMimeMemoryBudget(1000);
        var reservation = await budget.ReserveAsync(400, CancellationToken.None);

        // Act
        reservation.Dispose();
        reservation.Dispose();

        // Assert
        Assert.Equal(1000, budget.AvailableBytes);
    }
}
