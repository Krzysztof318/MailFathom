// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Host.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the resource the three signals of the container pipeline are composed with.</summary>
/// <remarks>
/// The resource is configured once and reaches logs, metrics, and traces through three separate providers, so the way
/// this can go wrong is one signal quietly missing what the other two carry. Nothing below a built provider answers
/// that — which is why this composes the pipeline rather than asserting the registration — and no server is started,
/// no request is served, and nothing is exported: the endpoint variable is absent from a builder created with defaults
/// disabled, so no exporter is attached.
/// </remarks>
public sealed class ServiceDefaultsExtensionsTests
{
    private static readonly ReplicaIdentity Replica = ReplicaIdentity.Create("mailfathom-0:1");

    [Fact]
    public void ConfigureOpenTelemetry_Always_NamesTheBuildOnLogsMetricsAndTracesAlike()
    {
        // Arrange
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var stampedVersion = StampedBuildResourceExtensions.StampedServiceVersion;
        var stampedRevision = StampedBuildResourceExtensions.StampedSourceRevision;

        // Act
        builder.ConfigureOpenTelemetry(Replica);

        using var host = builder.Build();

        // Assert
        Assert.Equal(stampedVersion, ReadServiceVersion(host.Services.GetRequiredService<LoggerProvider>()));
        Assert.Equal(stampedVersion, ReadServiceVersion(host.Services.GetRequiredService<MeterProvider>()));
        Assert.Equal(stampedVersion, ReadServiceVersion(host.Services.GetRequiredService<TracerProvider>()));
        Assert.Equal(stampedRevision, ReadSourceRevision(host.Services.GetRequiredService<LoggerProvider>()));
        Assert.Equal(stampedRevision, ReadSourceRevision(host.Services.GetRequiredService<MeterProvider>()));
        Assert.Equal(stampedRevision, ReadSourceRevision(host.Services.GetRequiredService<TracerProvider>()));
    }

    /// <summary>
    /// Several replicas export under one service name and one build, so the instance attribute is the only thing that
    /// keeps their series apart — and it has to be the value the lease rows and administrative answers carry.
    /// </summary>
    [Fact]
    public void ConfigureOpenTelemetry_WithNoInstanceSupplied_NamesTheReplicaOnLogsMetricsAndTracesAlike()
    {
        // Arrange
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });

        // Act
        builder.ConfigureOpenTelemetry(Replica);

        using var host = builder.Build();

        // Assert
        Assert.Equal(Replica.Value, ReadServiceInstance(host.Services.GetRequiredService<LoggerProvider>()));
        Assert.Equal(Replica.Value, ReadServiceInstance(host.Services.GetRequiredService<MeterProvider>()));
        Assert.Equal(Replica.Value, ReadServiceInstance(host.Services.GetRequiredService<TracerProvider>()));
    }

    /// <summary>
    /// How instances are named is the deployment's to decide, so an instance supplied through the standard variable is
    /// kept — while a build supplied the same way still loses to the stamped one.
    /// </summary>
    [Fact]
    public void ConfigureOpenTelemetry_WithAnInstanceInTheResourceAttributesVariable_KeepsTheSuppliedInstance()
    {
        // Arrange
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(
        [
            KeyValuePair.Create<string, string?>(
                "OTEL_RESOURCE_ATTRIBUTES",
                "service.instance.id=operator-named-instance,service.version=99.0.0-supplied"),
        ]);

        // Act
        builder.ConfigureOpenTelemetry(Replica);

        using var host = builder.Build();

        // Assert
        Assert.Equal("operator-named-instance", ReadServiceInstance(host.Services.GetRequiredService<LoggerProvider>()));
        Assert.Equal("operator-named-instance", ReadServiceInstance(host.Services.GetRequiredService<MeterProvider>()));
        Assert.Equal("operator-named-instance", ReadServiceInstance(host.Services.GetRequiredService<TracerProvider>()));
        Assert.Equal(
            StampedBuildResourceExtensions.StampedServiceVersion,
            ReadServiceVersion(host.Services.GetRequiredService<MeterProvider>()));
    }

    private static string ReadServiceInstance(BaseProvider provider) =>
        ReadAttribute(provider, ReplicaResourceExtensions.ServiceInstanceIdAttributeName);

    private static string ReadServiceVersion(BaseProvider provider) =>
        ReadAttribute(provider, StampedBuildResourceExtensions.ServiceVersionAttributeName);

    private static string ReadSourceRevision(BaseProvider provider) =>
        ReadAttribute(provider, StampedBuildResourceExtensions.SourceRevisionAttributeName);

    private static string ReadAttribute(BaseProvider provider, string attributeName)
    {
        var attribute = Assert.Single(
            provider.GetResource().Attributes,
            candidate => candidate.Key == attributeName);

        return Assert.IsType<string>(attribute.Value);
    }
}
