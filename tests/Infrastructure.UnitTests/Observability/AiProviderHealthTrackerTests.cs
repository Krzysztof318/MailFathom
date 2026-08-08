// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the state a capability gate and a health check both read, and that the two providers keep separate ones.</summary>
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
        var tracker = TrackerAt(Start);

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
        var time = new FakeTimeProvider(Start);
        var tracker = new AiProviderHealthTracker(time);

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
        var tracker = TrackerAt(Start);

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
        var tracker = TrackerAt(Start);

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
        var tracker = TrackerAt(Start);

        // Act
        tracker.RecordMisconfigured(AiProviderRole.Chat);

        // Assert
        Assert.Equal(AiProviderHealthState.Misconfigured, tracker.Read(AiProviderRole.Chat).State);
    }

    [Fact]
    public void Constructor_WithoutATimeProvider_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new AiProviderHealthTracker(null!));
    }

    private static AiProviderHealthTracker TrackerAt(DateTimeOffset moment) =>
        new(new FakeTimeProvider(moment));
}
