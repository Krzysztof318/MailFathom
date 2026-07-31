// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Failures;
using MailMcp.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers how a deployment states where it provisioned configuration.</summary>
public sealed class ProvisionedConfigurationPathsTests
{
    [Fact]
    public void ReadFrom_ConfiguredKeys_NamesBothProvisionedPaths()
    {
        // Arrange
        var configuration = Build(
            (ProvisionedConfigurationPaths.DirectoryKey, "/etc/mailmcp/config"),
            (ProvisionedConfigurationPaths.FileKey, "/etc/mailmcp/override.json"));

        // Act
        var paths = ProvisionedConfigurationPaths.ReadFrom(configuration);

        // Assert
        Assert.Equal(new ProvisionedConfigurationPaths("/etc/mailmcp/config", "/etc/mailmcp/override.json"), paths);
        Assert.True(paths.AreConfigured);
    }

    [Fact]
    public void ReadFrom_NoKeys_ProvisionsNothing()
    {
        // Arrange
        var configuration = Build();

        // Act
        var paths = ProvisionedConfigurationPaths.ReadFrom(configuration);

        // Assert
        Assert.Equal(new ProvisionedConfigurationPaths(null, null), paths);
        Assert.False(paths.AreConfigured);
    }

    /// <summary>Templating a manifest routinely emits an empty value for a setting the operator left unset.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ReadFrom_BlankValue_ProvisionsNothing(string configuredValue)
    {
        // Arrange
        var configuration = Build(
            (ProvisionedConfigurationPaths.DirectoryKey, configuredValue),
            (ProvisionedConfigurationPaths.FileKey, configuredValue));

        // Act
        var paths = ProvisionedConfigurationPaths.ReadFrom(configuration);

        // Assert
        Assert.False(paths.AreConfigured);
    }

    [Fact]
    public void ReadFrom_SurroundingWhitespace_NamesThePathWithoutIt()
    {
        // Arrange
        var configuration = Build((ProvisionedConfigurationPaths.DirectoryKey, "  /etc/mailmcp/config\n"));

        // Act
        var paths = ProvisionedConfigurationPaths.ReadFrom(configuration);

        // Assert
        Assert.Equal("/etc/mailmcp/config", paths.DirectoryPath);
    }

    /// <summary>A misspelling that bound nothing would leave the host on defaults with the mount believed in force.</summary>
    [Fact]
    public void ReadFrom_SettingMailMcpDoesNotDefine_FailsNamingIt()
    {
        // Arrange
        var configuration = Build(($"{ProvisionedConfigurationPaths.SectionName}:Directroy", "/etc/mailmcp/config"));

        // Act
        var failure = Assert.Throws<ProvisionedConfigurationSourceInvalidException>(
            () => ProvisionedConfigurationPaths.ReadFrom(configuration));

        // Assert
        Assert.Equal(MailMcpErrorCode.ProvisionedConfigurationSourceInvalid, failure.ErrorCode);
        Assert.Contains("Directroy", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A flattening provider can express a shape that is a defined name over no path at all.</summary>
    [Fact]
    public void ReadFrom_DefinedSettingCarryingNestedValues_FailsNamingIt()
    {
        // Arrange
        var configuration = Build(
            ($"{ProvisionedConfigurationPaths.DirectoryKey}:Path", "/etc/mailmcp/config"));

        // Act
        var failure = Assert.Throws<ProvisionedConfigurationSourceInvalidException>(
            () => ProvisionedConfigurationPaths.ReadFrom(configuration));

        // Assert
        Assert.Equal(MailMcpErrorCode.ProvisionedConfigurationSourceInvalid, failure.ErrorCode);
        Assert.Contains("Directory", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Configuration keys are case-insensitive, so a differently cased setting is the defined one.</summary>
    [Fact]
    public void ReadFrom_DifferentlyCasedSetting_NamesTheProvisionedPath()
    {
        // Arrange
        var configuration = Build(($"{ProvisionedConfigurationPaths.SectionName}:directory", "/etc/mailmcp/config"));

        // Act
        var paths = ProvisionedConfigurationPaths.ReadFrom(configuration);

        // Assert
        Assert.Equal("/etc/mailmcp/config", paths.DirectoryPath);
    }

    private static IConfiguration Build(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();
}
