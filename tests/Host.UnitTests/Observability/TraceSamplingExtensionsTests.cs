// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Host.Observability;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Trace;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability;

/// <summary>Covers which traces this host records and who gets to decide it.</summary>
/// <remarks>
/// Two claims are under test and the second is the one that can regress silently. The first is what an unconfigured
/// deployment records, which is everything it starts. The second is that a deployment that named a sampler is the one
/// deciding: the SDK discards its own configuration when a sampler was set programmatically and says so only to an
/// event source, so a host setting one unconditionally would answer <c>OTEL_TRACES_SAMPLER</c> with silence.
/// </remarks>
public sealed class TraceSamplingExtensionsTests
{
    /// <summary>A deployment that named no sampler gets the one this host chose, stated rather than inherited.</summary>
    [Fact]
    public void SamplerToSet_SamplerVariableAbsent_IsParentBasedAlwaysOn()
    {
        // Arrange
        var configuration = ConfigurationWith([]);

        // Act
        var sampler = TraceSamplingExtensions.SamplerToSet(configuration);

        // Assert
        Assert.IsType<ParentBasedSampler>(sampler);
        Assert.Contains("AlwaysOn", sampler.Description, StringComparison.Ordinal);
    }

    /// <summary>An empty value names no sampler, so it is the absent case rather than a sampler that drops nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SamplerToSet_SamplerVariableBlank_IsStillTheDefault(string configuredValue)
    {
        // Arrange
        var configuration = ConfigurationWith(
            [new KeyValuePair<string, string?>(TraceSamplingExtensions.SamplerVariableName, configuredValue)]);

        // Act
        var sampler = TraceSamplingExtensions.SamplerToSet(configuration);

        // Assert
        Assert.IsType<ParentBasedSampler>(sampler);
    }

    /// <summary>A deployment that named a sampler has this host set none, so the SDK builds the one it asked for.</summary>
    /// <remarks>
    /// Every value the specification defines is covered rather than one of them, because what is under test is that the
    /// variable is deferred to at all — this host does not parse it, and a value it does not recognize is the SDK's to
    /// reject.
    /// </remarks>
    [Theory]
    [InlineData("always_on")]
    [InlineData("always_off")]
    [InlineData("traceidratio")]
    [InlineData("parentbased_always_on")]
    [InlineData("parentbased_always_off")]
    [InlineData("parentbased_traceidratio")]
    [InlineData("something_the_sdk_will_reject")]
    public void SamplerToSet_SamplerVariableSet_LeavesTheSamplerToTheSdk(string configuredValue)
    {
        // Arrange
        var configuration = ConfigurationWith(
            [new KeyValuePair<string, string?>(TraceSamplingExtensions.SamplerVariableName, configuredValue)]);

        // Act
        var sampler = TraceSamplingExtensions.SamplerToSet(configuration);

        // Assert
        Assert.Null(sampler);
    }

    /// <summary>A trace this process starts is recorded, which is what makes a worker's own cycle diagnosable.</summary>
    [Fact]
    public void SamplerToSet_RootSpan_RecordsIt()
    {
        // Arrange
        var sampler = TraceSamplingExtensions.SamplerToSet(ConfigurationWith([]));
        var parameters = SamplingParametersFor(parentContext: null);

        // Act
        var result = sampler!.ShouldSample(in parameters);

        // Assert
        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    /// <summary>A caller that recorded its trace has this process record the part of it that happens here.</summary>
    [Fact]
    public void SamplerToSet_ChildOfARecordedTrace_RecordsIt()
    {
        // Arrange
        var sampler = TraceSamplingExtensions.SamplerToSet(ConfigurationWith([]));
        var parameters = SamplingParametersFor(ContextWith(ActivityTraceFlags.Recorded));

        // Act
        var result = sampler!.ShouldSample(in parameters);

        // Assert
        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    /// <summary>A caller that dropped its trace is not overruled here, which is what parent-based buys.</summary>
    [Fact]
    public void SamplerToSet_ChildOfADroppedTrace_DropsIt()
    {
        // Arrange
        var sampler = TraceSamplingExtensions.SamplerToSet(ConfigurationWith([]));
        var parameters = SamplingParametersFor(ContextWith(ActivityTraceFlags.None));

        // Act
        var result = sampler!.ShouldSample(in parameters);

        // Assert
        Assert.Equal(SamplingDecision.Drop, result.Decision);
    }

    private static IConfiguration ConfigurationWith(IReadOnlyList<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>Builds a parent a caller already decided about, recorded or not.</summary>
    private static ActivityContext ContextWith(ActivityTraceFlags traceFlags) =>
        new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), traceFlags);

    /// <summary>Builds the question a sampler is asked, with no parent where the trace starts here.</summary>
    private static SamplingParameters SamplingParametersFor(ActivityContext? parentContext) =>
        new(
            parentContext: parentContext ?? default,
            traceId: ActivityTraceId.CreateRandom(),
            name: "probe",
            kind: ActivityKind.Internal);
}
