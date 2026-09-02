// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers the process-wide connection ceiling and how it is read from configuration.</summary>
/// <remarks>
/// This is the one transport bound that belongs to the process rather than to a surface, so the cases worth stating are
/// that it applies without being configured, that it can be turned off deliberately, and that a misspelled key cannot
/// leave a deployment believing it raised a ceiling that never moved.
/// </remarks>
public sealed class ConnectionLimitsOptionsTests
{
    [Fact]
    public void Enabled_WithNothingConfigured_BoundsTheProcess()
    {
        // Act
        var settings = new ConnectionLimitsOptions();

        // Assert
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void MaxConcurrentConnections_WithNothingConfigured_IsFarAboveWhatTheEndpointLimitsKeepBusy()
    {
        // Act
        var settings = new ConnectionLimitsOptions();

        // Assert
        // A connection is not a request: a client holds one open across several, so the ceiling has to sit well above
        // the request limits or it would refuse ordinary clients long before it refused a flood.
        Assert.True(settings.MaxConcurrentConnections >= 1000);
    }

    [Fact]
    public void ReadFrom_WithNothingConfigured_IsTheProductDefault()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var settings = ConnectionLimitsOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.Enabled);
        Assert.Equal(new ConnectionLimitsOptions().MaxConcurrentConnections, settings.MaxConcurrentConnections);
    }

    [Fact]
    public void ReadFrom_WithAConfiguredCeiling_BindsIt()
    {
        // Arrange
        var configuration = ConfigurationWith(("ConnectionLimits:MaxConcurrentConnections", "250"));

        // Act
        var settings = ConnectionLimitsOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(250, settings.MaxConcurrentConnections);
    }

    [Fact]
    public void ReadFrom_WithAnUnknownKey_Throws()
    {
        // Arrange
        // Strict binding is what stops a deployment reading as bounded while the key it wrote bound nothing.
        var configuration = ConfigurationWith(("ConnectionLimits:MaxConnections", "250"));

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => ConnectionLimitsOptions.ReadFrom(configuration));
    }

    [Fact]
    public void ReadFrom_WithNoConfiguration_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => ConnectionLimitsOptions.ReadFrom(null!));
    }

    [Fact]
    public void FindConfigurationErrors_WithNothingConfigured_ReportsNothing()
    {
        // Arrange
        var settings = new ConnectionLimitsOptions();

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_WithTheCeilingTurnedOff_ReportsNothingAboutTheCount()
    {
        // Arrange
        var settings = new ConnectionLimitsOptions { Enabled = false, MaxConcurrentConnections = 0 };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void FindConfigurationErrors_WithACeilingOutsideTheRange_ReportsTheSetting(int maxConcurrentConnections)
    {
        // Arrange
        var settings = new ConnectionLimitsOptions { MaxConcurrentConnections = maxConcurrentConnections };

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ConnectionLimitsOptions.SectionName}:{nameof(ConnectionLimitsOptions.MaxConcurrentConnections)}",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_WithACeilingOutsideTheRange_NamesWhatToWriteToAcceptConnectionsUnbounded()
    {
        // Arrange
        var settings = new ConnectionLimitsOptions { MaxConcurrentConnections = 0 };

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ConnectionLimitsOptions.SectionName}:{nameof(ConnectionLimitsOptions.Enabled)}",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(100_000)]
    public void FindConfigurationErrors_WithACeilingInsideTheRange_ReportsNothing(int maxConcurrentConnections)
    {
        // Arrange
        var settings = new ConnectionLimitsOptions { MaxConcurrentConnections = maxConcurrentConnections };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    private static IConfiguration ConfigurationWith(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();
}
