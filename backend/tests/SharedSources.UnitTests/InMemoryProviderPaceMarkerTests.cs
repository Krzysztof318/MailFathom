// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the shared pace marker, whose arithmetic every rate-ceiling test is measured against.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement: a marker that handed every caller the same slot would make a
/// pacer look unpaced, and one that never advanced would hold a burst that should have been spread.
/// </remarks>
public sealed class InMemoryProviderPaceMarkerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReserveNextSlotAsync_TheFirstCallerOfAnIdleWorkload_WaitsForNothing()
    {
        // Arrange
        var marker = new InMemoryProviderPaceMarker(new FakeTimeProvider(Start));

        // Act
        var wait = await marker.ReserveNextSlotAsync(
            ProviderPacedWorkloads.EmailEmbedding,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.Zero, wait);
        Assert.Equal(1, marker.ReservationCount);
    }

    [Fact]
    public async Task ReserveNextSlotAsync_ABurstOnOneWorkload_HandsOutOneIntervalMoreEachTime()
    {
        // Arrange
        var marker = new InMemoryProviderPaceMarker(new FakeTimeProvider(Start));
        var interval = TimeSpan.FromSeconds(1);

        // Act
        var waits = new List<TimeSpan>();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            waits.Add(await marker.ReserveNextSlotAsync(
                ProviderPacedWorkloads.EmailEmbedding,
                interval,
                TestContext.Current.CancellationToken));
        }

        // Assert
        Assert.Equal([TimeSpan.Zero, interval, interval * 2], waits);
    }

    /// <summary>A marker left behind by an idle stretch never owes the caller the slots nobody took.</summary>
    [Fact]
    public async Task ReserveNextSlotAsync_TheMarkerIsAlreadyBehindTheClock_WaitsForNothing()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var marker = new InMemoryProviderPaceMarker(clock);
        await marker.ReserveNextSlotAsync(
            ProviderPacedWorkloads.EmailEmbedding,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromMinutes(5));
        var wait = await marker.ReserveNextSlotAsync(
            ProviderPacedWorkloads.EmailEmbedding,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.Zero, wait);
    }

    /// <summary>Two workloads pace independently, exactly as their two rows do.</summary>
    [Fact]
    public async Task ReserveNextSlotAsync_ASecondWorkload_TakesItsOwnSlotsRatherThanTheFirstsRemaining()
    {
        // Arrange
        var marker = new InMemoryProviderPaceMarker(new FakeTimeProvider(Start));
        var interval = TimeSpan.FromSeconds(1);
        await marker.ReserveNextSlotAsync(ProviderPacedWorkloads.EmailEmbedding, interval, TestContext.Current.CancellationToken);

        // Act
        var wait = await marker.ReserveNextSlotAsync(
            ProviderPacedWorkloads.AttachmentImageDescription,
            interval,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    public async Task ReserveNextSlotAsync_AnArgumentNoSlotCouldBeReservedFor_IsRefused()
    {
        // Arrange
        var marker = new InMemoryProviderPaceMarker(new FakeTimeProvider(Start));

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => marker.ReserveNextSlotAsync(string.Empty, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => marker.ReserveNextSlotAsync(
                ProviderPacedWorkloads.EmailEmbedding,
                TimeSpan.Zero,
                TestContext.Current.CancellationToken));
    }
}
