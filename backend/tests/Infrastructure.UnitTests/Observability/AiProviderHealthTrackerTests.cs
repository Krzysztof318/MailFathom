// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the state a capability gate and a health check both read, that the two providers keep separate ones, and what a transition says.</summary>
public sealed class AiProviderHealthTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A freshly started instance has failed at nothing, so nothing may read as a failure.</summary>
    [Theory]
    [InlineData(AiProviderRole.Embedding)]
    [InlineData(AiProviderRole.Chat)]
    public void Read_BeforeAnyCall_IsUnobserved(AiProviderRole role)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        // Act
        var health = tracker.Read(role);

        // Assert
        Assert.Equal(role, health.Role);
        Assert.Equal(AiProviderHealthState.Unobserved, health.State);
        Assert.Null(health.ObservedAt);
    }

    [Fact]
    public void RecordServed_ACall_StampsTheStateWithWhenItEnded()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var time = new FakeTimeProvider(Start);
        var tracker = TrackerOver(time, logs);

        time.Advance(TimeSpan.FromMinutes(3));

        // Act
        tracker.RecordServed(AiProviderRole.Chat);

        // Assert
        var health = tracker.Read(AiProviderRole.Chat);

        Assert.Equal(AiProviderHealthState.Serving, health.State);
        Assert.Equal(Start.AddMinutes(3), health.ObservedAt);
    }

    /// <summary>
    /// The two providers are declared, called, and fail independently, so a chat outage must never read as an embedding
    /// one. This is the whole reason the state is keyed by role rather than held as one flag.
    /// </summary>
    [Fact]
    public void Record_OneProvider_LeavesTheOtherUntouched()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        // Act
        tracker.RecordUnavailable(AiProviderRole.Chat);

        // Assert
        Assert.Equal(AiProviderHealthState.Unavailable, tracker.Read(AiProviderRole.Chat).State);
        Assert.Equal(AiProviderHealthState.Unobserved, tracker.Read(AiProviderRole.Embedding).State);
    }

    /// <summary>What is reported is the last call rather than the worst one, so a provider that recovered reads as recovered.</summary>
    [Fact]
    public void Record_ACallAfterAFailure_ReportsTheLatestOutcome()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        tracker.RecordMisconfigured(AiProviderRole.Embedding);

        // Act
        tracker.RecordServed(AiProviderRole.Embedding);

        // Assert
        Assert.Equal(AiProviderHealthState.Serving, tracker.Read(AiProviderRole.Embedding).State);
    }

    [Fact]
    public void RecordMisconfigured_ACallNobodyCanWaitOut_SeparatesItFromAnOutage()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        // Act
        tracker.RecordMisconfigured(AiProviderRole.Chat);

        // Assert
        Assert.Equal(AiProviderHealthState.Misconfigured, tracker.Read(AiProviderRole.Chat).State);
    }

    /// <summary>The line an operator reads to know a capability was withdrawn, and which of the two answers fixes it.</summary>
    [Fact]
    public void RecordMisconfigured_TheFirstFailure_LogsTheTransitionWithTheRoleAndTheClassification()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        // Act
        tracker.RecordMisconfigured(AiProviderRole.Embedding);

        // Assert
        var record = Assert.Single(logs.Records);

        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(AiProviderRole.Embedding, record.Properties["AiProviderRole"]);
        Assert.Equal(AiProviderHealthState.Unobserved, record.Properties["PreviousState"]);
        Assert.Equal(AiProviderHealthState.Misconfigured, record.Properties["State"]);
    }

    /// <summary>Recovery is what an operator watching a degraded instance is waiting for, so it is stated rather than inferred from silence.</summary>
    [Fact]
    public void RecordServed_AfterAFailure_LogsTheRecoveryAtInformation()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        tracker.RecordUnavailable(AiProviderRole.Embedding);

        // Act
        tracker.RecordServed(AiProviderRole.Embedding);

        // Assert
        var record = logs.Records.Last();

        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(AiProviderRole.Embedding, record.Properties["AiProviderRole"]);
        Assert.Equal(AiProviderHealthState.Unavailable, record.Properties["PreviousState"]);
    }

    /// <summary>A first call that worked restored nothing, so reporting it as a recovery would be a line on every start.</summary>
    [Fact]
    public void RecordServed_TheFirstCallAnInstanceMakes_LogsNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        // Act
        tracker.RecordServed(AiProviderRole.Embedding);

        // Assert
        Assert.Empty(logs.Records);
        Assert.Equal(AiProviderHealthState.Serving, tracker.Read(AiProviderRole.Embedding).State);
    }

    /// <summary>Every provider call records, so a line per call would put the log's volume on the mailbox's size.</summary>
    [Fact]
    public void Record_TheSameStateAgain_LogsNothingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var tracker = TrackerAt(Start, logs);

        tracker.RecordUnavailable(AiProviderRole.Embedding);

        // Act
        tracker.RecordUnavailable(AiProviderRole.Embedding);
        tracker.RecordUnavailable(AiProviderRole.Embedding);

        // Assert
        Assert.Single(logs.Records);
    }

    [Fact]
    public void Constructor_WithoutATimeProvider_IsRefused()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new AiProviderHealthTracker(
            null!,
            loggerFactory.CreateLogger<AiProviderHealthTracker>()));
    }

    [Fact]
    public void Constructor_WithoutALogger_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AiProviderHealthTracker(new FakeTimeProvider(Start), null!));
    }

    private static AiProviderHealthTracker TrackerAt(DateTimeOffset moment, RecordingLoggerProvider logs) =>
        TrackerOver(new FakeTimeProvider(moment), logs);

    private static AiProviderHealthTracker TrackerOver(TimeProvider timeProvider, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        return new AiProviderHealthTracker(timeProvider, loggerFactory.CreateLogger<AiProviderHealthTracker>());
    }
}
