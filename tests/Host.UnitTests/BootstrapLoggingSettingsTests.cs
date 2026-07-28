// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Observability;
using Xunit;

namespace MailMcp.Host.UnitTests;

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
        Assert.Equal("MailMcp.Host", settings.ServiceName);
    }

    [Fact]
    public void From_ServiceNameConfigured_PrefersTheNameTheOrchestratorInjected()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom((ServiceNameVariable, "mailmcp-host")));

        // Assert
        Assert.Equal("mailmcp-host", settings.ServiceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_ServiceNameBlank_FallsBackToTheHostAssemblyName(string configuredServiceName)
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom((ServiceNameVariable, configuredServiceName)));

        // Assert
        Assert.Equal("MailMcp.Host", settings.ServiceName);
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

    [Fact]
    public void From_Always_ReportsAHostVersionCarryingNoSourceControlBuildMetadata()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(ReadFrom());

        // Assert
        Assert.NotEmpty(settings.ServiceVersion);
        Assert.DoesNotContain("+", settings.ServiceVersion, StringComparison.Ordinal);
    }

    private static Func<string, string?> ReadFrom(params (string Name, string? Value)[] variables)
    {
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);

        return name => environment.GetValueOrDefault(name);
    }
}
