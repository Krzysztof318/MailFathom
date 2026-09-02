// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using MailFathom.Common.Observability;
using MailFathom.Host.Observability;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using OpenTelemetry.Metrics;
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

    /// <summary>The name the AI telemetry decorators publish under when a client is built without one of its own.</summary>
    /// <remarks>
    /// Read from the library for the same reason the MCP SDK's is, and with more at stake: the AI boundary passes no
    /// source name to <c>UseOpenTelemetry</c>, so what its spans and instruments arrive under is entirely the library's
    /// default. A rename under a package bump would leave every provider call uncollected while the code went on
    /// looking instrumented, and the declaration is internal, so reflection is what turns that into a failing test
    /// here. One name serves both registries because the decorators construct their activity source and their meter
    /// from the same string; a release that split them would fail the meter assertion rather than pass it silently.
    /// </remarks>
    private static string DeclaredExtensionsAiTelemetryName
    {
        get
        {
            var declaration = typeof(OpenTelemetryChatClient).Assembly
                .GetType("Microsoft.Extensions.AI.OpenTelemetryConsts", throwOnError: true)!
                .GetField("DefaultSourceName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.NotNull(declaration);

            return Assert.IsType<string>(declaration.GetValue(null));
        }
    }

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
        Assert.Equal(
            ["Experimental.ModelContextProtocol", "Experimental.Microsoft.Extensions.AI"],
            tracing.SubscribedSources);
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
            [
                "Polly",
                "Experimental.ModelContextProtocol",
                "Microsoft.EntityFrameworkCore",
                "Experimental.Microsoft.Extensions.AI",
            ],
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

    /// <summary>
    /// The transport limiter's refusals are reported under the framework's own rate-limiting meter, which the ASP.NET
    /// Core instrumentation supplies rather than <see cref="TelemetrySubscriptionExtensions.AddLibraryMeters" />. That
    /// is why the name appears in no registration here and why it is asserted instead: it arrives with a package
    /// version, an instrumentation release that stopped subscribing it would leave every 429 uncounted, and nothing in
    /// this process would say so. <c>TransportSurface.RateLimitingPolicyName</c> is documented as the tag an operator
    /// reads a refusal by, and this is what keeps that true.
    /// </summary>
    [Fact]
    public void AddAspNetCoreInstrumentation_SubscribesTheMeterTransportRefusalsAreCountedOn()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();

        // Act
        metrics.AddAspNetCoreInstrumentation();

        // Assert
        Assert.Contains("Microsoft.AspNetCore.RateLimiting", metrics.SubscribedMeters);
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
    public void AddLibraryActivitySources_SubscribesTheNameTheAiDecoratorsStartTheirSpansFrom()
    {
        // Arrange
        var tracing = new RecordingTracerProviderBuilder();

        // Act
        tracing.AddLibraryActivitySources();

        // Assert
        Assert.Contains(DeclaredExtensionsAiTelemetryName, tracing.SubscribedSources);
    }

    [Fact]
    public void AddLibraryMeters_SubscribesTheNameTheAiDecoratorsCreateTheirInstrumentsOn()
    {
        // Arrange
        var metrics = new RecordingMeterProviderBuilder();

        // Act
        metrics.AddLibraryMeters();

        // Assert
        Assert.Contains(DeclaredExtensionsAiTelemetryName, metrics.SubscribedMeters);
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
