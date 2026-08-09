// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability;
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
    [Fact]
    public void ConfigureOpenTelemetry_Always_NamesTheBuildOnLogsMetricsAndTracesAlike()
    {
        // Arrange
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var stampedVersion = ServiceVersionResourceExtensions.StampedServiceVersion;

        // Act
        builder.ConfigureOpenTelemetry();

        using var host = builder.Build();

        // Assert
        Assert.Equal(stampedVersion, ReadServiceVersion(host.Services.GetRequiredService<LoggerProvider>()));
        Assert.Equal(stampedVersion, ReadServiceVersion(host.Services.GetRequiredService<MeterProvider>()));
        Assert.Equal(stampedVersion, ReadServiceVersion(host.Services.GetRequiredService<TracerProvider>()));
    }

    private static string ReadServiceVersion(BaseProvider provider)
    {
        var attribute = Assert.Single(
            provider.GetResource().Attributes,
            candidate => candidate.Key == ServiceVersionResourceExtensions.ServiceVersionAttributeName);

        return Assert.IsType<string>(attribute.Value);
    }
}
