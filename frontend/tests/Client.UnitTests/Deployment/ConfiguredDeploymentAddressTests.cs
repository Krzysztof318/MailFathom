// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Deployment;

namespace MailFathom.Client.UnitTests.Deployment;

/// <summary>Covers how an installed head reads the deployment it was told to reach.</summary>
/// <remarks>
/// Three claims, and the first two matter more than they look. An installation stating nothing fails rather than
/// reaching a default, because the default anyone would pick is somebody else's deployment; and a value with no scheme
/// is read as HTTPS, because reading it as clear text would turn an omission into a token handed to whatever is on the
/// path. Whether a clear-text address is acceptable at all is <c>Client.Backend</c>'s rule and is asserted with it.
/// </remarks>
public sealed class ConfiguredDeploymentAddressTests
{
    [Fact]
    public void Resolve_AnOriginStatedInFull_IsTheAddressEveryRouteResolvesAgainst()
    {
        // Arrange
        var source = new ConfiguredDeploymentAddress();

        // Act
        var address = source.Resolve(new DeploymentSettings { Address = "https://mail.example.test:8443" });

        // Assert
        Assert.Equal(new Uri("https://mail.example.test:8443"), address);
    }

    /// <summary>An omission is read as the safe scheme rather than as the one it happens to resemble.</summary>
    [Theory]
    [InlineData("mail.example.test")]
    [InlineData("//mail.example.test")]
    [InlineData("  mail.example.test  ")]
    public void Resolve_AnOriginStatedWithoutAScheme_IsReadAsHttps(string stated)
    {
        // Arrange
        var source = new ConfiguredDeploymentAddress();

        // Act
        var address = source.Resolve(new DeploymentSettings { Address = stated });

        // Assert
        Assert.Equal(new Uri("https://mail.example.test"), address);
    }

    /// <summary>The failure a head with no deployment has to end on, naming the setting rather than opening a window that cannot explain itself.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_AnInstallationStatingNoAddress_FailsNamingTheSetting(string stated)
    {
        // Arrange
        var source = new ConfiguredDeploymentAddress();

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => source.Resolve(new DeploymentSettings { Address = stated }));

        // Assert
        Assert.Contains(
            $"{DeploymentSettings.SectionName}:{nameof(DeploymentSettings.Address)}",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>A mistyped address is the same failure rather than a silent fallback, and the message repeats what was written so it can be found.</summary>
    [Fact]
    public void Resolve_AnAddressNothingCanBeReachedAt_FailsRepeatingWhatWasWritten()
    {
        // Arrange
        const string stated = "ht!tp://mail.example.test";
        var source = new ConfiguredDeploymentAddress();

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => source.Resolve(new DeploymentSettings { Address = stated }));

        // Assert
        Assert.Contains(stated, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_NoSettings_IsRefused()
    {
        // Arrange
        var source = new ConfiguredDeploymentAddress();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => source.Resolve(null!));
    }
}
