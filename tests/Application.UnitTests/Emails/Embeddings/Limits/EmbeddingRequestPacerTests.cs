// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Limits;

/// <summary>Covers the rate ceiling binding on a caller and releasing it once its slot arrives.</summary>
public sealed class EmbeddingRequestPacerTests
{
    [Fact]
    public async Task WaitForSlotAsync_NoRateIsDeclared_LetsEveryCallerThroughAtOnce()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var pacer = EmbeddingRequestPacer.Create(maxRequestsPerMinute: 0, timeProvider);

        // Act
        var waits = Enumerable
            .Range(0, 10)
            .Select(_ => pacer.WaitForSlotAsync(TestContext.Current.CancellationToken))
            .ToArray();

        // Assert
        Assert.True(pacer.IsUnpaced);
        Assert.All(waits, wait => Assert.True(wait.IsCompletedSuccessfully));
        await Task.WhenAll(waits);
    }

    /// <summary>The first caller of an idle pacer is not held back; a rate bounds a burst rather than every request.</summary>
    [Fact]
    public async Task WaitForSlotAsync_TheFirstCaller_IsNotHeldBack()
    {
        // Arrange
        var pacer = EmbeddingRequestPacer.Create(maxRequestsPerMinute: 60, new FakeTimeProvider());

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
        var pacer = EmbeddingRequestPacer.Create(maxRequestsPerMinute: 60, timeProvider);
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
        var pacer = EmbeddingRequestPacer.Create(maxRequestsPerMinute: 60, timeProvider);

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

    /// <summary>A wait the host abandons ends as a cancellation rather than holding the shutdown open.</summary>
    [Fact]
    public async Task WaitForSlotAsync_TheCallerIsCancelledWhileWaiting_EndsTheWait()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var pacer = EmbeddingRequestPacer.Create(maxRequestsPerMinute: 60, timeProvider);
        await pacer.WaitForSlotAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        // Act
        var second = pacer.WaitForSlotAsync(cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [Fact]
    public void Create_ARateThatCouldNotPaceAnything_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmbeddingRequestPacer.Create(maxRequestsPerMinute: -1, new FakeTimeProvider()));
        Assert.Throws<ArgumentNullException>(
            () => EmbeddingRequestPacer.Create(maxRequestsPerMinute: 60, null!));
    }
}
