// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class BootstrapLoggingSettingsTests
{
    private const string ServiceNameKey = "OTEL_SERVICE_NAME";
    private const string ExporterEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    [Fact]
    public void From_NoConfiguredServiceName_NamesTheServiceAfterTheApplication()
    {
        // Arrange
        var configuration = CreateConfiguration();
        var environment = CreateEnvironment(applicationName: "MailMcp.Host");

        // Act
        var settings = BootstrapLoggingSettings.From(configuration, environment);

        // Assert
        Assert.Equal("MailMcp.Host", settings.ServiceName);
    }

    [Fact]
    public void From_ServiceNameConfigured_PrefersItSoBootstrapAndHostRecordsShareOneIdentity()
    {
        // Arrange
        var configuration = CreateConfiguration((ServiceNameKey, "mailmcp-host"));
        var environment = CreateEnvironment(applicationName: "MailMcp.Host");

        // Act
        var settings = BootstrapLoggingSettings.From(configuration, environment);

        // Assert
        Assert.Equal("mailmcp-host", settings.ServiceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_ServiceNameBlank_FallsBackToTheApplicationName(string configuredServiceName)
    {
        // Arrange
        var configuration = CreateConfiguration((ServiceNameKey, configuredServiceName));
        var environment = CreateEnvironment(applicationName: "MailMcp.Host");

        // Act
        var settings = BootstrapLoggingSettings.From(configuration, environment);

        // Assert
        Assert.Equal("MailMcp.Host", settings.ServiceName);
    }

    [Fact]
    public void From_ExporterEndpointConfigured_ExportsToTheCollector()
    {
        // Arrange
        var configuration = CreateConfiguration((ExporterEndpointKey, "http://localhost:4317"));

        // Act
        var settings = BootstrapLoggingSettings.From(configuration, CreateEnvironment());

        // Assert
        Assert.True(settings.ExportsToCollector);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_ExporterEndpointMissingOrBlank_LeavesTheExporterUnregistered(string? configuredEndpoint)
    {
        // Arrange
        var configuration = CreateConfiguration((ExporterEndpointKey, configuredEndpoint));

        // Act
        var settings = BootstrapLoggingSettings.From(configuration, CreateEnvironment());

        // Assert
        Assert.False(settings.ExportsToCollector);
    }

    [Fact]
    public void From_Always_ReportsTheEnvironmentTheHostWasStartedIn()
    {
        // Arrange
        var environment = CreateEnvironment(environmentName: "Staging");

        // Act
        var settings = BootstrapLoggingSettings.From(CreateConfiguration(), environment);

        // Assert
        Assert.Equal("Staging", settings.EnvironmentName);
    }

    [Fact]
    public void From_Always_ReportsAHostVersionCarryingNoSourceControlBuildMetadata()
    {
        // Act
        var settings = BootstrapLoggingSettings.From(CreateConfiguration(), CreateEnvironment());

        // Assert
        Assert.NotEmpty(settings.ServiceVersion);
        Assert.DoesNotContain("+", settings.ServiceVersion, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([.. values.Select(value => KeyValuePair.Create(value.Key, value.Value))])
            .Build();

    private static IHostEnvironment CreateEnvironment(
        string applicationName = "MailMcp.Host",
        string environmentName = "Production")
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns(applicationName);
        environment.EnvironmentName.Returns(environmentName);

        return environment;
    }
}
