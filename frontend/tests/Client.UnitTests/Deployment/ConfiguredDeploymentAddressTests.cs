// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Deployment;

namespace MailFathom.Client.UnitTests.Deployment;

/// <summary>Covers how an installed head reads the deployment it was told to reach.</summary>
/// <remarks>
/// The two claims that matter are the two ways of saying nothing usable, and they are deliberately different answers.
/// An installation that stated no address at all is the ordinary state of a fresh one, so it answers nothing and the
/// client asks whoever is using it; an installation that stated something unreadable is a value somebody wrote and
/// would otherwise never learn was ignored, so it fails. The third claim is that a value with no scheme is read as
/// HTTPS, because reading it as clear text would turn an omission into a sign-in handed to whatever is on the path.
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

    /// <summary>A fresh installation is one nobody has configured, and the client's answer to that is to ask rather than to fail.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_AnInstallationStatingNoAddress_HasNothingToSay(string stated)
    {
        // Arrange
        var source = new ConfiguredDeploymentAddress();

        // Act
        var address = source.Resolve(new DeploymentSettings { Address = stated });

        // Assert
        Assert.Null(address);
    }

    /// <summary>A mistyped address is a failure rather than a silent fallback, and the message repeats what was written so it can be found.</summary>
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
        Assert.Contains(
            $"{DeploymentSettings.SectionName}:{nameof(DeploymentSettings.Address)}",
            failure.Message,
            StringComparison.Ordinal);
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
