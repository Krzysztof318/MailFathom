// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using MailFathom.Common.Observability;
using MailFathom.Host.Observability;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using ModelContextProtocol.Server;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability;

/// <summary>Covers that every name this process is supposed to collect is actually subscribed.</summary>
/// <remarks>
/// The failure this guards against is silent in production: something publishes spans to a source nothing subscribed,
/// the code looks instrumented, and the trace store holds nothing. Asserting the subscribed set is what makes a name
/// dropped from the registration a failing test rather than a quiet gap — for MailFathom's own declaration, and for the
/// libraries whose names arrive with a package version and can change under one.
/// </remarks>
public sealed class TelemetrySubscriptionExtensionsTests
{
    /// <summary>The MCP SDK's internal declaration of the one name it publishes both registries under.</summary>
    /// <remarks>
    /// Reflection is used here deliberately and nowhere else: the declaration is internal to the SDK, and the name it
    /// holds is marked experimental by the package that owns it, so a rename is a realistic outcome of a version bump.
    /// Reading it makes that rename a compile-or-test failure at the moment the pin moves, instead of an MCP surface
    /// that silently stops appearing in traces and metrics on the next deployment. A missing type is the same signal:
    /// the SDK's telemetry has moved and the subscription needs re-verifying against its current source.
    /// </remarks>
    private static Type ModelContextProtocolDiagnostics =>
        typeof(McpServerTool).Assembly.GetType("ModelContextProtocol.Diagnostics", throwOnError: true)!;

    [Fact]
    public void AddMailFathomActivitySources_SubscribesTheDeclaredName()
    {
        // Arrange
        var tracing = new RecordingTracerProviderBuilder();

        // Act
        tracing.AddMailFathomActivitySources();

        // Assert
        Assert.Equal([Telemetry.Name], tracing.SubscribedSources);
    }

    [Fact]
    public void AddMailFathomMeters_SubscribesTheDeclaredName()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();

        // Act
        metrics.AddMailFathomMeters();

        // Assert
        Assert.Equal([Telemetry.Name], metrics.SubscribedMeters);
    }

    [Fact]
    public void AddLibraryActivitySources_SubscribesEveryLibraryThatPublishesSpansUnderItsOwnName()
    {
        // Arrange
        var tracing = new RecordingTracerProviderBuilder();

        // Act
        tracing.AddLibraryActivitySources();

        // Assert
        Assert.Equal(["Experimental.ModelContextProtocol"], tracing.SubscribedSources);
    }

    [Fact]
    public void AddLibraryMeters_SubscribesEveryLibraryThatPublishesInstrumentsUnderItsOwnName()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();

        // Act
        metrics.AddLibraryMeters();

        // Assert
        Assert.Equal(
            ["Polly", "Experimental.ModelContextProtocol", "Microsoft.EntityFrameworkCore"],
            metrics.SubscribedMeters);
    }

    [Fact]
    public void AddLibraryMeters_LeavesTheMeterTheDatabaseEnrichmentAlreadySubscribes()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();

        // Act
        metrics.AddLibraryMeters();

        // Assert
        Assert.DoesNotContain("Npgsql", metrics.SubscribedMeters);
    }

    [Fact]
    public void AddLibraryActivitySources_SubscribesTheNameTheMcpSdkStartsItsSpansFrom()
    {
        // Arrange
        var tracing = new RecordingTracerProviderBuilder();
        var declaredName = ReadDeclaredTelemetryName("ActivitySource");

        // Act
        tracing.AddLibraryActivitySources();

        // Assert
        Assert.Contains(declaredName, tracing.SubscribedSources);
    }

    [Fact]
    public void AddLibraryMeters_SubscribesTheNameTheMcpSdkCreatesItsInstrumentsOn()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();
        var declaredName = ReadDeclaredTelemetryName("Meter");

        // Act
        metrics.AddLibraryMeters();

        // Assert
        Assert.Contains(declaredName, metrics.SubscribedMeters);
    }

    [Fact]
    public void AddLibraryMeters_SubscribesTheNameEntityFrameworkCoreDeclaresForItsMeter()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();

        // Act
        metrics.AddLibraryMeters();

        // Assert
        // EF1001 warns that this declaration may change without notice, which is the hazard being asserted rather than
        // a reason to look away from it: a meter renamed under a version bump would otherwise leave the subscription
        // collecting nothing and say so nowhere. Reading the declaration turns that into a failure here, and its
        // removal into one at compile time. The suppression covers this assertion alone.
#pragma warning disable EF1001
        Assert.Contains(EntityFrameworkMetrics.MeterName, metrics.SubscribedMeters);
#pragma warning restore EF1001
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

    [Fact]
    public void AddLibraryActivitySources_WithNoBuilder_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => TelemetrySubscriptionExtensions.AddLibraryActivitySources(null!));
    }

    [Fact]
    public void AddLibraryMeters_WithNoBuilder_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => TelemetrySubscriptionExtensions.AddLibraryMeters(null!));
    }

    private static string ReadDeclaredTelemetryName(string memberName)
    {
        var declaration = ModelContextProtocolDiagnostics.GetProperty(
            memberName,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(declaration);

        return declaration.GetValue(null) switch
        {
            ActivitySource source => source.Name,
            Meter meter => meter.Name,
            var unexpected => throw new InvalidOperationException(
                $"The MCP SDK's {memberName} declaration is no longer a telemetry registry but {unexpected?.GetType()}."),
        };
    }
}
