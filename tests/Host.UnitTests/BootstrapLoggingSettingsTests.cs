// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability;
using MailFathom.Versioning;
using Xunit;

namespace MailFathom.Host.UnitTests;

public sealed class BootstrapLoggingSettingsTests
{
    private const string ServiceNameVariable = "OTEL_SERVICE_NAME";
    private const string ExporterEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
    private const string DotnetEnvironmentVariable = "DOTNET_ENVIRONMENT";

    [Fact]
    public void From_NoConfiguredServiceName_NamesTheServiceAfterTheHostAssembly()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom());

        // Assert
        Assert.Equal("MailFathom.Host", settings.ServiceName);
    }

    [Fact]
    public void From_ServiceNameConfigured_PrefersTheNameTheOrchestratorInjected()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom((ServiceNameVariable, "mailfathom-host")));

        // Assert
        Assert.Equal("mailfathom-host", settings.ServiceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_ServiceNameBlank_FallsBackToTheHostAssemblyName(string configuredServiceName)
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom((ServiceNameVariable, configuredServiceName)));

        // Assert
        Assert.Equal("MailFathom.Host", settings.ServiceName);
    }

    [Fact]
    public void From_ExporterEndpointConfigured_ExportsToTheCollector()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(
            ReadFrom((ExporterEndpointVariable, "http://localhost:4317")));

        // Assert
        Assert.True(settings.ExportsToCollector);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_ExporterEndpointMissingOrBlank_LeavesTheExporterUnregistered(string? configuredEndpoint)
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom((ExporterEndpointVariable, configuredEndpoint)));

        // Assert
        Assert.False(settings.ExportsToCollector);
    }

    [Fact]
    public void From_AspNetCoreEnvironmentSet_ReportsTheEnvironmentTheHostWillSelect()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom((AspNetCoreEnvironmentVariable, "Staging")));

        // Assert
        Assert.Equal("Staging", settings.EnvironmentName);
    }

    [Fact]
    public void From_OnlyDotnetEnvironmentSet_FallsBackToItTheWayTheHostDoes()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom((DotnetEnvironmentVariable, "Staging")));

        // Assert
        Assert.Equal("Staging", settings.EnvironmentName);
    }

    [Fact]
    public void From_BothEnvironmentVariablesSet_PrefersTheAspNetCoreOne()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(
            ReadFrom((AspNetCoreEnvironmentVariable, "Staging"), (DotnetEnvironmentVariable, "Development")));

        // Assert
        Assert.Equal("Staging", settings.EnvironmentName);
    }

    [Fact]
    public void From_NoEnvironmentConfigured_ReportsProduction()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom());

        // Assert
        Assert.Equal("Production", settings.EnvironmentName);
    }

    /// <summary>
    /// The expectation is read from the host assembly's own build-time metadata rather than restated here, so a
    /// reporting path that regressed to a literal — one that would stay plausible while the declared version moved —
    /// fails this instead of passing it. The revision is asserted with it because the two are stamped together and
    /// only reported apart.
    /// </summary>
    [Fact]
    public void From_Always_ReportsTheVersionAndRevisionStampedIntoTheHostAssembly()
    {
        // Arrange
        var stamped = StampedAssemblyVersion.ReadFrom(typeof(BootstrapLoggingSettings).Assembly);

        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom());

        // Assert
        Assert.Equal(stamped.Version, settings.ServiceVersion);
        Assert.Equal(stamped.Revision, settings.ServiceRevision);
        Assert.NotEmpty(settings.ServiceVersion);
        Assert.DoesNotContain("+", settings.ServiceVersion, StringComparison.Ordinal);
    }

    private static Func<string, string?> ReadFrom(params (string Name, string? Value)[] variables)
    {
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);

        return name => environment.GetValueOrDefault(name);
    }
}
