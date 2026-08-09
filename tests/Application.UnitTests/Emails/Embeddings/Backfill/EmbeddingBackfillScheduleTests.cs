// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Backfill;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Backfill;

/// <summary>Covers the pause between upkeep passes: when it ends, and the one act that ends it early.</summary>
public sealed class EmbeddingBackfillScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan IdleSweepInterval = TimeSpan.FromMinutes(15);

    /// <summary>The ordinary pause: nothing asked for a pass, so the wait lasts exactly as long as it was given.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_ThePauseElapses_ReportsThatNothingBroughtThePassForward()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var schedule = new EmbeddingBackfillSchedule(clock);

        // Act
        var waiting = schedule.WaitForNextPassAsync(IdleSweepInterval, TestContext.Current.CancellationToken);
        clock.Advance(IdleSweepInterval);

        // Assert
        Assert.False(await waiting);
    }

    /// <summary>
    /// The whole point of the type. An activation writes a row the sleeping worker cannot observe, so without this the
    /// first pass of a reindex waits out an idle interval an earlier pass chose while there was nothing to embed.
    /// </summary>
    [Fact]
    public async Task WaitForNextPassAsync_BroughtForwardWhileWaiting_EndsTheWaitWithoutTheClockMoving()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var schedule = new EmbeddingBackfillSchedule(clock);

        // Act
        var waiting = schedule.WaitForNextPassAsync(IdleSweepInterval, TestContext.Current.CancellationToken);
        schedule.BringForward();

        // Assert
        Assert.True(await waiting);
        Assert.Equal(Now, schedule.NextPassDueAt);
    }

    /// <summary>
    /// An activation that lands while a pass is already running has nobody to release, and dropping it would leave the
    /// work it created waiting for the next pause to expire — which is the delay this type exists to remove.
    /// </summary>
    [Fact]
    public async Task WaitForNextPassAsync_BroughtForwardWhileAPassWasRunning_IsTakenByTheNextWaitRatherThanLost()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var schedule = new EmbeddingBackfillSchedule(clock);
        schedule.BringForward();

        // Act
        var broughtForward = await schedule.WaitForNextPassAsync(
            IdleSweepInterval,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(broughtForward);
    }

    /// <summary>One request is one pass, so a request nobody has taken is not answered twice.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_TheWaitAfterOneBroughtForward_PausesAgainRatherThanEndingImmediately()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var schedule = new EmbeddingBackfillSchedule(clock);
        schedule.BringForward();
        await schedule.WaitForNextPassAsync(IdleSweepInterval, TestContext.Current.CancellationToken);

        // Act
        var waiting = schedule.WaitForNextPassAsync(IdleSweepInterval, TestContext.Current.CancellationToken);
        var dueAtWhileWaiting = schedule.NextPassDueAt;
        clock.Advance(IdleSweepInterval);

        // Assert
        Assert.False(await waiting);
        Assert.Equal(Now + IdleSweepInterval, dueAtWhileWaiting);
    }

    /// <summary>An instance whose worker has scheduled nothing reports no pass, which is what a disabled backfill looks like.</summary>
    [Fact]
    public void NextPassDueAt_NoPassEverScheduled_ReportsNone()
    {
        // Arrange
        var schedule = new EmbeddingBackfillSchedule(new FakeTimeProvider(Now));

        // Act
        var dueAt = schedule.NextPassDueAt;

        // Assert
        Assert.Null(dueAt);
    }

    /// <summary>
    /// A deployment whose walk is turned off has no worker to release, so recording a due instant for an activation
    /// would leave the status surface reporting a pass that is overdue for the life of the process — the same
    /// unreadable state this type exists to remove, arrived at from the other side.
    /// </summary>
    [Fact]
    public void BringForward_WhereNoPassWillRun_SchedulesNothing()
    {
        // Arrange
        var schedule = new EmbeddingBackfillSchedule(new FakeTimeProvider(Now));
        schedule.NoPassWillRun();

        // Act
        schedule.BringForward();

        // Assert
        Assert.Null(schedule.NextPassDueAt);
    }

    /// <summary>
    /// An activation can reach the process before its worker has run far enough to report that it takes no pass, so
    /// the report clears what got in first rather than leaving one stale instant behind.
    /// </summary>
    [Fact]
    public void NoPassWillRun_APassAlreadyAskedFor_ClearsIt()
    {
        // Arrange
        var schedule = new EmbeddingBackfillSchedule(new FakeTimeProvider(Now));
        schedule.BringForward();

        // Act
        schedule.NoPassWillRun();

        // Assert
        Assert.Null(schedule.NextPassDueAt);
    }

    /// <summary>The latched request goes with the instant, so a walk that runs no pass cannot answer one either.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_AfterNoPassWillRunClearedARequest_PausesRatherThanReturningImmediately()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var schedule = new EmbeddingBackfillSchedule(clock);
        schedule.BringForward();
        schedule.NoPassWillRun();

        // Act
        var waiting = schedule.WaitForNextPassAsync(IdleSweepInterval, TestContext.Current.CancellationToken);
        clock.Advance(IdleSweepInterval);

        // Assert
        Assert.False(await waiting);
    }

    /// <summary>A stopping process ends the wait rather than taking one more pass on the way out.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_TheProcessStopping_EndsTheWaitAsCancelled()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var schedule = new EmbeddingBackfillSchedule(clock);
        using var stopping = new CancellationTokenSource();

        // Act
        var waiting = schedule.WaitForNextPassAsync(IdleSweepInterval, stopping.Token);
        await stopping.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
