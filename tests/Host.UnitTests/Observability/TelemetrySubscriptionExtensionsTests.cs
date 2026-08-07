// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.Observability;
using MailFathom.Host.Observability;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability;

/// <summary>Covers that the name MailFathom declares for itself is actually collected.</summary>
/// <remarks>
/// The failure this guards against is silent in production: a subsystem publishes spans to a source nothing subscribed,
/// the code looks instrumented, and the trace store holds nothing. Asserting the subscribed name against the
/// declaration is what makes the host and the publishers disagreeing a failing test rather than a quiet gap.
/// </remarks>
public sealed class TelemetrySubscriptionExtensionsTests
{
    [Fact]
    public void AddMailFathomActivitySources_SubscribesTheDeclaredName()
    {
        // Arrange
        var tracing = new RecordingTracerProviderBuilder();

        // Act
        tracing.AddMailFathomActivitySources();

        // Assert
        Assert.Equal([MailFathomTelemetry.Name], tracing.SubscribedSources);
    }

    [Fact]
    public void AddMailFathomMeters_SubscribesTheDeclaredName()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();

        // Act
        metrics.AddMailFathomMeters();

        // Assert
        Assert.Equal([MailFathomTelemetry.Name], metrics.SubscribedMeters);
    }

    [Fact]
    public void AddMailFathomActivitySources_WithNoBuilder_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => TelemetrySubscriptionExtensions.AddMailFathomActivitySources(null!));
    }

    [Fact]
    public void AddMailFathomMeters_WithNoBuilder_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => TelemetrySubscriptionExtensions.AddMailFathomMeters(null!));
    }
}
