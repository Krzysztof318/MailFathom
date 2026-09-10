// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the ceiling every answering run of one deployment shares, and how the period turns over.</summary>
/// <remarks>
/// The window is decided by the clock this tracker is given, so every test drives a fake one: what is proved is when an
/// allowance returns, never how long a test waited. What the allowance is counted in is the ledger behind it rather
/// than a field of this process, which is what lets a test hold two replicas against one period.
/// </remarks>
public sealed class MailAnsweringSpendTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryAdmitRunAsync_APeriodWithAnAllowanceLeft_AdmitsTheRunAndCountsIt()
    {
        // Arrange
        var (tracker, _, periods) = TrackerAllowing(runs: 2);

        // Act
        var admitted = await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(admitted);
        Assert.Equal(1, tracker.Read().Runs);
        Assert.Equal(1, periods.Spent(Start).Runs);
    }

    /// <summary>Nothing about the MCP surface stops a client from asking a hundred questions in a minute, and this is what does.</summary>
    [Fact]
    public async Task TryAdmitRunAsync_APeriodThatHasAdmittedItsRuns_RefusesTheNextQuestion()
    {
        // Arrange
        var (tracker, _, _) = TrackerAllowing(runs: 2);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Act
        var admitted = await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(admitted);
        Assert.Equal(2, tracker.Read().Runs);
    }

    /// <summary>
    /// The ceiling is the deployment's rather than each process's, which is the defect this change is about: two
    /// replicas reading their own counters would each admit the whole allowance and spend twice what was configured.
    /// </summary>
    [Fact]
    public async Task TryAdmitRunAsync_AnotherReplicaHasSpentThePeriod_RefusesTheQuestionHere()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();
        var clock = new FakeTimeProvider(Start);
        var onOneReplica = Tracker(periods, clock, runs: 1);
        var onAnother = Tracker(periods, clock, runs: 1);

        // Act
        var admittedThere = await onOneReplica.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        var admittedHere = await onAnother.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(admittedThere);
        Assert.False(admittedHere);
        Assert.Equal(1, periods.Spent(Start).Runs);
    }

    /// <summary>What another replica spent is what this one's gauges publish once it has read the period itself.</summary>
    [Fact]
    public async Task Read_AnotherReplicaSpentThePeriod_PublishesTheDeploymentsTokensRatherThanItsOwn()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();
        var clock = new FakeTimeProvider(Start);
        var onOneReplica = Tracker(periods, clock, runs: 10, tokens: 1_000);
        var onAnother = Tracker(periods, clock, runs: 10, tokens: 1_000);
        await onOneReplica.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await onOneReplica.RecordSpendAsync(new ChatTokenUsage(50, 20), TestContext.Current.CancellationToken);

        // Act
        await onAnother.RecordSpendAsync(new ChatTokenUsage(30, 0), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(100L, onAnother.Read().Tokens);
    }

    /// <summary>A refused run takes no allowance, so a client that keeps asking cannot push the period's count past its ceiling.</summary>
    [Fact]
    public async Task TryAdmitRunAsync_AQuestionRefusedTwice_LeavesTheCountAtTheCeiling()
    {
        // Arrange
        var (tracker, _, periods) = TrackerAllowing(runs: 1);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Act
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, tracker.Read().Runs);
        Assert.Equal(1, periods.Spent(Start).Runs);
    }

    [Fact]
    public async Task TryAdmitRunAsync_APeriodThatHasConsumedItsTokens_RefusesTheNextQuestion()
    {
        // Arrange
        var (tracker, _, _) = TrackerAllowing(runs: 100, tokens: 100);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await tracker.RecordSpendAsync(new ChatTokenUsage(70, 40), TestContext.Current.CancellationToken);

        // Act
        var admitted = await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(admitted);
    }

    [Fact]
    public async Task TryAdmitRunAsync_APeriodUnderItsTokenCeiling_StillAdmits()
    {
        // Arrange
        var (tracker, _, _) = TrackerAllowing(runs: 100, tokens: 100);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await tracker.RecordSpendAsync(new ChatTokenUsage(40, 30), TestContext.Current.CancellationToken);

        // Act, Assert
        Assert.True(await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The window turns over on its own, which is what makes a refused question worth asking again later.</summary>
    [Fact]
    public async Task TryAdmitRunAsync_APeriodThatHasElapsed_AdmitsAgainAndForgetsWhatWasSpent()
    {
        // Arrange
        var (tracker, clock, periods) = TrackerAllowing(runs: 1, tokens: 100);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await tracker.RecordSpendAsync(new ChatTokenUsage(90, 30), TestContext.Current.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromHours(1));
        var admitted = await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(admitted);
        Assert.Equal(2, periods.PeriodCount);

        var spend = tracker.Read();

        Assert.Equal(1, spend.Runs);
        Assert.Equal(0L, spend.Tokens);
    }

    /// <summary>A window that has not elapsed keeps counting, so a burst spread over a period is still one period's spend.</summary>
    [Fact]
    public async Task TryAdmitRunAsync_APeriodPartlyElapsed_KeepsCountingTheSameOne()
    {
        // Arrange
        var (tracker, clock, _) = TrackerAllowing(runs: 1);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromMinutes(59));

        // Assert
        Assert.False(await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Start, tracker.Read().PeriodStartedAt);
    }

    /// <summary>
    /// The window is where the bounds place the clock rather than an hour from the last reset, so the windows an idle
    /// instance skipped are skipped rather than owed to it.
    /// </summary>
    [Fact]
    public async Task Read_SeveralPeriodsThatElapsedWhileNothingWasAsked_CountsUnderTheCurrentOneRatherThanTheNext()
    {
        // Arrange
        var (tracker, clock, _) = TrackerAllowing(runs: 1);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

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
            ScopesOver(new InMemoryMailAnsweringSpendPeriodStore()),
            new FakeTimeProvider(startedMidPeriod),
            NullLogger<MailAnsweringSpendTracker>.Instance);

        // Act
        var spend = tracker.Read();

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero), spend.PeriodStartedAt);
    }

    /// <summary>A call is charged to the window it finished in, which is the same rule the admission uses.</summary>
    [Fact]
    public async Task RecordSpendAsync_ACallThatFinishedAfterTheWindowRolledOver_ChargesTheWindowItFinishedIn()
    {
        // Arrange
        var (tracker, clock, periods) = TrackerAllowing(runs: 10, tokens: 1_000);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(1));

        // Act
        await tracker.RecordSpendAsync(new ChatTokenUsage(50, 20), TestContext.Current.CancellationToken);

        // Assert
        var spend = tracker.Read();

        Assert.Equal(70L, spend.Tokens);
        Assert.Equal(0, spend.Runs);
        Assert.Equal(0L, periods.Spent(Start).Tokens);
        Assert.Equal(70L, periods.Spent(Start + TimeSpan.FromHours(1)).Tokens);
    }

    /// <summary>
    /// A client that keeps asking is exactly what spends a period's allowance, so a line per refusal would put the
    /// log's volume on how enthusiastic that client is. The counter carries how often it happened.
    /// </summary>
    [Fact]
    public async Task TryAdmitRunAsync_ManyRefusalsInOnePeriod_WritesOneLineRatherThanOnePerRefusal()
    {
        // Arrange
        using var logging = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logging));
        var clock = new FakeTimeProvider(Start);
        var tracker = new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 1, 300_000),
            ScopesOver(new InMemoryMailAnsweringSpendPeriodStore()),
            clock,
            loggerFactory.CreateLogger<MailAnsweringSpendTracker>());
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Act
        foreach (var _ in Enumerable.Range(1, 20))
        {
            await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.Single(logging.Records, record => record.Level is LogLevel.Warning);
    }

    /// <summary>The line says that refusals started, so a period that starts refusing again is worth one more.</summary>
    [Fact]
    public async Task TryAdmitRunAsync_ARefusalInEachOfTwoPeriods_WritesOneLinePerPeriod()
    {
        // Arrange
        using var logging = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logging));
        var clock = new FakeTimeProvider(Start);
        var tracker = new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 1, 300_000),
            ScopesOver(new InMemoryMailAnsweringSpendPeriodStore()),
            clock,
            loggerFactory.CreateLogger<MailAnsweringSpendTracker>());

        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromHours(1));
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);
        await tracker.TryAdmitRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logging.Records.Count(record => record.Level is LogLevel.Warning));
    }

    [Fact]
    public async Task RecordSpendAsync_WithoutUsage_IsRefused()
    {
        // Arrange
        var (tracker, _, _) = TrackerAllowing();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => tracker.RecordSpendAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_WithoutACollaborator_IsRefused()
    {
        // Arrange
        var scopes = ScopesOver(new InMemoryMailAnsweringSpendPeriodStore());

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringSpendTracker(
            null!,
            scopes,
            new FakeTimeProvider(Start),
            NullLogger<MailAnsweringSpendTracker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Default,
            null!,
            new FakeTimeProvider(Start),
            NullLogger<MailAnsweringSpendTracker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Default,
            scopes,
            null!,
            NullLogger<MailAnsweringSpendTracker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringSpendTracker(
            MailAnsweringPeriodBounds.Default,
            scopes,
            new FakeTimeProvider(Start),
            null!));
    }

    private static (MailAnsweringSpendTracker Tracker, FakeTimeProvider Clock, InMemoryMailAnsweringSpendPeriodStore Periods) TrackerAllowing(
        int runs = 30,
        long tokens = 300_000)
    {
        var clock = new FakeTimeProvider(Start);
        var periods = new InMemoryMailAnsweringSpendPeriodStore();

        return (Tracker(periods, clock, runs, tokens), clock, periods);
    }

    private static MailAnsweringSpendTracker Tracker(
        InMemoryMailAnsweringSpendPeriodStore periods,
        FakeTimeProvider clock,
        int runs = 30,
        long tokens = 300_000) =>
        new(
            MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), runs, tokens),
            ScopesOver(periods),
            clock,
            NullLogger<MailAnsweringSpendTracker>.Instance);

    /// <summary>Opens the scopes the tracker writes each admission and each spend through, over one in-memory ledger.</summary>
    /// <remarks>
    /// A real container rather than a substituted factory, because what the tracker does with the scope is resolve a
    /// service out of it: a substitute would have to answer the resolution too, and the assertion would then be about
    /// the substitute's script rather than about the ledger.
    /// </remarks>
    private static IServiceScopeFactory ScopesOver(IMailAnsweringSpendPeriodStore periods)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => periods);

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
