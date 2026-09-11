// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.AiProviders;

/// <summary>Covers the rate ceiling binding on a caller and releasing it once the deployment's slot arrives.</summary>
public sealed class ProviderRequestPacerTests
{
    [Fact]
    public async Task WaitForSlotAsync_NoRateIsDeclared_LetsEveryCallerThroughWithoutReservingAnything()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var marker = new InMemoryProviderPaceMarker(timeProvider);
        var pacer = Pacer(maxRequestsPerMinute: 0, marker, timeProvider);

        // Act
        var waits = Enumerable
            .Range(0, 10)
            .Select(_ => pacer.WaitForSlotAsync(TestContext.Current.CancellationToken))
            .ToArray();

        // Assert
        Assert.True(pacer.IsUnpaced);
        Assert.All(waits, wait => Assert.True(wait.IsCompletedSuccessfully));
        Assert.Equal(0, marker.ReservationCount);
        await Task.WhenAll(waits);
    }

    /// <summary>The first caller of an idle pacer is not held back; a rate bounds a burst rather than every request.</summary>
    [Fact]
    public async Task WaitForSlotAsync_TheFirstCaller_IsNotHeldBack()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var pacer = Pacer(maxRequestsPerMinute: 60, new InMemoryProviderPaceMarker(timeProvider), timeProvider);

        // Act
        var wait = pacer.WaitForSlotAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(wait.IsCompletedSuccessfully);
        await wait;
    }

    /// <summary>
    /// The ceiling is reached and then released by the clock alone: the second caller waits exactly one interval and
    /// completes when it has passed, with nobody having released anything.
    /// </summary>
    [Fact]
    public async Task WaitForSlotAsync_ASecondCallerInsideTheInterval_WaitsForItsOwnSlotAndThenProceeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var pacer = Pacer(maxRequestsPerMinute: 60, new InMemoryProviderPaceMarker(timeProvider), timeProvider);
        await pacer.WaitForSlotAsync(TestContext.Current.CancellationToken);

        // Act
        var second = pacer.WaitForSlotAsync(TestContext.Current.CancellationToken);
        var pendingBeforeTheSlot = second.IsCompleted;
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.False(pendingBeforeTheSlot);
        await second;
    }

    /// <summary>
    /// Slots are handed out in order rather than to whoever asks after the wait, so a burst of callers is spread across
    /// the rate instead of every one of them waking on the first interval.
    /// </summary>
    [Fact]
    public async Task WaitForSlotAsync_ABurstOfCallers_TakesOneSlotEachRatherThanSharingOne()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var pacer = Pacer(maxRequestsPerMinute: 60, new InMemoryProviderPaceMarker(timeProvider), timeProvider);

        // Act
        var waits = Enumerable
            .Range(0, 3)
            .Select(_ => pacer.WaitForSlotAsync(TestContext.Current.CancellationToken))
            .ToArray();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var completedAfterOneInterval = waits.Count(wait => wait.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(2, completedAfterOneInterval);
        await Task.WhenAll(waits);
    }

    /// <summary>
    /// A second replica pacing the same workload waits behind the slots this one took, because the marker they both
    /// reserve against is the deployment's rather than either process's.
    /// </summary>
    [Fact]
    public async Task WaitForSlotAsync_ASecondReplicaPacingTheSameWorkload_WaitsBehindTheSlotsTheFirstTook()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var marker = new InMemoryProviderPaceMarker(timeProvider);
        var onOneReplica = Pacer(maxRequestsPerMinute: 60, marker, timeProvider);
        var onAnother = Pacer(maxRequestsPerMinute: 60, marker, timeProvider);
        await onOneReplica.WaitForSlotAsync(TestContext.Current.CancellationToken);

        // Act
        var elsewhere = onAnother.WaitForSlotAsync(TestContext.Current.CancellationToken);
        var pendingBeforeTheSlot = elsewhere.IsCompleted;
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.False(pendingBeforeTheSlot);
        Assert.Equal(2, marker.ReservationCount);
        await elsewhere;
    }

    /// <summary>Two workloads carry two rates, so describing pictures never spends an embedding run's slots.</summary>
    [Fact]
    public async Task WaitForSlotAsync_ASecondWorkloadPacedByTheSameMarker_TakesItsOwnSlots()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var marker = new InMemoryProviderPaceMarker(timeProvider);
        var embedding = Pacer(maxRequestsPerMinute: 60, marker, timeProvider);
        var imageDescription = ProviderRequestPacer.Create(
            ProviderPacedWorkloads.AttachmentImageDescription,
            maxRequestsPerMinute: 60,
            marker,
            timeProvider);
        await embedding.WaitForSlotAsync(TestContext.Current.CancellationToken);

        // Act
        var describing = imageDescription.WaitForSlotAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(describing.IsCompletedSuccessfully);
        await describing;
    }

    /// <summary>A wait the host abandons ends as a cancellation rather than holding the shutdown open.</summary>
    [Fact]
    public async Task WaitForSlotAsync_TheCallerIsCancelledWhileWaiting_EndsTheWait()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var pacer = Pacer(maxRequestsPerMinute: 60, new InMemoryProviderPaceMarker(timeProvider), timeProvider);
        await pacer.WaitForSlotAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        // Act
        var second = pacer.WaitForSlotAsync(cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [Fact]
    public void Create_ARateOrACollaboratorThatCouldNotPaceAnything_IsRefused()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var marker = new InMemoryProviderPaceMarker(timeProvider);

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ProviderRequestPacer.Create(string.Empty, maxRequestsPerMinute: 60, marker, timeProvider));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pacer(maxRequestsPerMinute: -1, marker, timeProvider));
        Assert.Throws<ArgumentNullException>(
            () => ProviderRequestPacer.Create(
                ProviderPacedWorkloads.EmailEmbedding,
                maxRequestsPerMinute: 60,
                null!,
                timeProvider));
        Assert.Throws<ArgumentNullException>(
            () => ProviderRequestPacer.Create(
                ProviderPacedWorkloads.EmailEmbedding,
                maxRequestsPerMinute: 60,
                marker,
                null!));
    }

    private static ProviderRequestPacer Pacer(
        int maxRequestsPerMinute,
        IProviderPaceMarker marker,
        TimeProvider timeProvider) =>
        ProviderRequestPacer.Create(
            ProviderPacedWorkloads.EmailEmbedding,
            maxRequestsPerMinute,
            marker,
            timeProvider);
}
