// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the ceiling every answering run of one period shares, and how the period turns over.</summary>
/// <remarks>
/// The window is decided by the clock this tracker is given, so every test drives a fake one: what is proved is when an
/// allowance returns, never how long a test waited.
/// </remarks>
public sealed class MailAnsweringSpendTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAdmitRun_APeriodWithAnAllowanceLeft_AdmitsTheRunAndCountsIt()
    {
        // Arrange
        var (tracker, _) = TrackerAllowing(runs: 2);

        // Act
        var admitted = tracker.TryAdmitRun();

        // Assert
        Assert.True(admitted);
        Assert.Equal(1, tracker.Read().Runs);
    }

    /// <summary>Nothing about the MCP surface stops a client from asking a hundred questions in a minute, and this is what does.</summary>
    [Fact]
    public void TryAdmitRun_APeriodThatHasAdmittedItsRuns_RefusesTheNextQuestion()
    {
        // Arrange
        var (tracker, _) = TrackerAllowing(runs: 2);
        tracker.TryAdmitRun();
        tracker.TryAdmitRun();

        // Act
        var admitted = tracker.TryAdmitRun();

        // Assert
        Assert.False(admitted);
        Assert.Equal(2, tracker.Read().Runs);
    }

    /// <summary>A refused run takes no allowance, so a client that keeps asking cannot push the period's count past its ceiling.</summary>
    [Fact]
    public void TryAdmitRun_AQuestionRefusedTwice_LeavesTheCountAtTheCeiling()
    {
        // Arrange
        var (tracker, _) = TrackerAllowing(runs: 1);
        tracker.TryAdmitRun();

        // Act
        tracker.TryAdmitRun();
        tracker.TryAdmitRun();

        // Assert
        Assert.Equal(1, tracker.Read().Runs);
    }

    [Fact]
    public void TryAdmitRun_APeriodThatHasConsumedItsTokens_RefusesTheNextQuestion()
    {
        // Arrange
        var (tracker, _) = TrackerAllowing(runs: 100, tokens: 100);
        tracker.TryAdmitRun();
        tracker.RecordSpend(new ChatTokenUsage(70, 40));

        // Act
        var admitted = tracker.TryAdmitRun();

        // Assert
        Assert.False(admitted);
    }

    [Fact]
    public void TryAdmitRun_APeriodUnderItsTokenCeiling_StillAdmits()
    {
        // Arrange
        var (tracker, _) = TrackerAllowing(runs: 100, tokens: 100);
        tracker.TryAdmitRun();
        tracker.RecordSpend(new ChatTokenUsage(40, 30));

        // Act, Assert
        Assert.True(tracker.TryAdmitRun());
    }

    /// <summary>The window turns over on its own, which is what makes a refused question worth asking again later.</summary>
    [Fact]
    public void TryAdmitRun_APeriodThatHasElapsed_AdmitsAgainAndForgetsWhatWasSpent()
    {
        // Arrange
        var (tracker, clock) = TrackerAllowing(runs: 1, tokens: 100);
        tracker.TryAdmitRun();
        tracker.RecordSpend(new ChatTokenUsage(90, 30));

        // Act
        clock.Advance(TimeSpan.FromHours(1));
        var admitted = tracker.TryAdmitRun();

        // Assert
        Assert.True(admitted);

        var spend = tracker.Read();

        Assert.Equal(1, spend.Runs);
        Assert.Equal(0L, spend.Tokens);
    }

    /// <summary>A window that has not elapsed keeps counting, so a burst spread over a period is still one period's spend.</summary>
    [Fact]
    public void TryAdmitRun_APeriodPartlyElapsed_KeepsCountingTheSameOne()
    {
        // Arrange
        var (tracker, clock) = TrackerAllowing(runs: 1);
        tracker.TryAdmitRun();

        // Act
        clock.Advance(TimeSpan.FromMinutes(59));

        // Assert
        Assert.False(tracker.TryAdmitRun());
        Assert.Equal(Start, tracker.Read().PeriodStartedAt);
    }

    /// <summary>
    /// The window is where the bounds place the clock rather than an hour from the last reset, so the windows an idle
    /// instance skipped are skipped rather than owed to it.
    /// </summary>
    [Fact]
    public void Read_SeveralPeriodsThatElapsedWhileNothingWasAsked_CountsUnderTheCurrentOneRatherThanTheNext()
    {
        // Arrange
        var (tracker, clock) = TrackerAllowing(runs: 1);
        tracker.TryAdmitRun();

        // Act
        clock.Advance(TimeSpan.FromHours(5));
        var spend = tracker.Read();

        // Assert
        Assert.Equal(MailAnsweringPeriodBounds.Default.PeriodStartAt(Start + TimeSpan.FromHours(5)), spend.PeriodStartedAt);
        Assert.Equal(0, spend.Runs);
    }

    /// <summary>The window is anchored at the epoch rather than at start-up, so two processes of one deployment count against the same boundaries.</summary>
    [Fact]
    public void Read_ATrackerStartedMidPeriod_CountsUnderTheBoundaryTheClockPlacesRatherThanItsOwnStart()
    {
        // Arrange
        var startedMidPeriod = new DateTimeOffset(2026, 8, 8, 12, 37, 41, TimeSpan.Zero);
        var tracker = new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 30, 300_000),
            new FakeTimeProvider(startedMidPeriod),
            NullLogger<MailAnsweringSpendTracker>.Instance);

        // Act
        var spend = tracker.Read();

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero), spend.PeriodStartedAt);
    }

    /// <summary>A call is charged to the window it finished in, which is the same rule the admission uses.</summary>
    [Fact]
    public void RecordSpend_ACallThatFinishedAfterTheWindowRolledOver_ChargesTheWindowItFinishedIn()
    {
        // Arrange
        var (tracker, clock) = TrackerAllowing(runs: 10, tokens: 1_000);
        tracker.TryAdmitRun();
        clock.Advance(TimeSpan.FromHours(1));

        // Act
        tracker.RecordSpend(new ChatTokenUsage(50, 20));

        // Assert
        var spend = tracker.Read();

        Assert.Equal(70L, spend.Tokens);
        Assert.Equal(0, spend.Runs);
    }

    [Fact]
    public void Read_APeriodWithSpend_ReportsBothDirectionsSeparately()
    {
        // Arrange
        var (tracker, _) = TrackerAllowing(runs: 10, tokens: 1_000);
        tracker.TryAdmitRun();
        tracker.RecordSpend(new ChatTokenUsage(50, 20));

        // Act
        var spend = tracker.Read();

        // Assert
        Assert.Equal(50L, spend.InputTokens);
        Assert.Equal(20L, spend.OutputTokens);
        Assert.Equal(70L, spend.Tokens);
    }

    /// <summary>
    /// A client that keeps asking is exactly what spends a period's allowance, so a line per refusal would put the
    /// log's volume on how enthusiastic that client is. The counter carries how often it happened.
    /// </summary>
    [Fact]
    public void TryAdmitRun_ManyRefusalsInOnePeriod_WritesOneLineRatherThanOnePerRefusal()
    {
        // Arrange
        using var logging = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logging));
        var clock = new FakeTimeProvider(Start);
        var tracker = new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 1, 300_000),
            clock,
            loggerFactory.CreateLogger<MailAnsweringSpendTracker>());
        tracker.TryAdmitRun();

        // Act
        Enumerable.Range(1, 20).ToList().ForEach(_ => tracker.TryAdmitRun());

        // Assert
        Assert.Single(logging.Records, record => record.Level is LogLevel.Warning);
    }

    /// <summary>The line says that refusals started, so a period that starts refusing again is worth one more.</summary>
    [Fact]
    public void TryAdmitRun_ARefusalInEachOfTwoPeriods_WritesOneLinePerPeriod()
    {
        // Arrange
        using var logging = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logging));
        var clock = new FakeTimeProvider(Start);
        var tracker = new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 1, 300_000),
            clock,
            loggerFactory.CreateLogger<MailAnsweringSpendTracker>());

        tracker.TryAdmitRun();
        tracker.TryAdmitRun();

        // Act
        clock.Advance(TimeSpan.FromHours(1));
        tracker.TryAdmitRun();
        tracker.TryAdmitRun();

        // Assert
        Assert.Equal(2, logging.Records.Count(record => record.Level is LogLevel.Warning));
    }

    [Fact]
    public void RecordSpend_WithoutUsage_IsRefused()
    {
        // Arrange
        var (tracker, _) = TrackerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => tracker.RecordSpend(null!));
    }

    [Fact]
    public void Constructor_WithoutACollaborator_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringSpendTracker(
            null!,
            new FakeTimeProvider(Start),
            NullLogger<MailAnsweringSpendTracker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Default,
            null!,
            NullLogger<MailAnsweringSpendTracker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Default,
            new FakeTimeProvider(Start),
            null!));
    }

    private static (MailAnsweringSpendTracker Tracker, FakeTimeProvider Clock) TrackerAllowing(
        int runs = 30,
        long tokens = 300_000)
    {
        var clock = new FakeTimeProvider(Start);
        var tracker = new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), runs, tokens),
            clock,
            NullLogger<MailAnsweringSpendTracker>.Instance);

        return (tracker, clock);
    }
}
