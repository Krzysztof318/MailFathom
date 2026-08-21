// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.Resolution;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets;

/// <summary>Covers the identity and lifetime every discovered secret has to declare before a deployment can use it.</summary>
/// <remarks>
/// The rules live on the walk's result rather than on a consumer because uniqueness is a property of the whole set, and
/// no single block can answer it. Their scope is one walk, which is one bound configuration root.
/// </remarks>
public sealed class SecretDeclarationTests
{
    [Fact]
    public void FindDeclarationErrors_WellDeclaredSecrets_ReportNothing()
    {
        // Arrange
        var discovered = Discover(
            ("MailSynchronization:Accounts:0:Secrets:Password", Named("primary-password")),
            ("MailSynchronization:Accounts:1:Secrets:Password", Named("secondary-password")));

        // Act
        var errors = discovered.FindDeclarationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindDeclarationErrors_ASecretWithNoName_ReportsItAgainstItsConfigurationPath()
    {
        // Arrange
        var discovered = Discover(("Persistence:Password", new ConfiguredSecret()));

        // Act
        var error = Assert.Single(discovered.FindDeclarationErrors());

        // Assert
        Assert.Equal("Persistence:Password", error.ConfigurationPath);
        Assert.Equal(SecretDeclarationFailure.NameMissing, error.Failure);
    }

    [Fact]
    public void FindDeclarationErrors_ASecretWhoseNameIsNotAcceptable_ReportsItAsMalformedRatherThanMissing()
    {
        // Arrange
        var discovered = Discover(("Persistence:Password", Named("the postgres password")));

        // Act
        var error = Assert.Single(discovered.FindDeclarationErrors());

        // Assert
        Assert.Equal(SecretDeclarationFailure.NameMalformed, error.Failure);
    }

    /// <summary>A name that identifies two secrets identifies neither, which is exactly when a rotation instruction stops being actionable.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoSecretsSharingAName_ReportsTheSecondOne()
    {
        // Arrange
        var discovered = Discover(
            ("McpEndpoint:ApiKeys:0", Named("workstation")),
            ("McpEndpoint:ApiKeys:1", Named("workstation")));

        // Act
        var error = Assert.Single(discovered.FindDeclarationErrors());

        // Assert
        Assert.Equal("McpEndpoint:ApiKeys:1", error.ConfigurationPath);
        Assert.Equal(SecretDeclarationFailure.NameDuplicated, error.Failure);
    }

    /// <summary>Two names differing only in case are one identity to everyone who reads them.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoNamesDifferingOnlyInCase_AreTreatedAsOneName()
    {
        // Arrange
        var discovered = Discover(
            ("McpEndpoint:ApiKeys:0", Named("workstation")),
            ("McpEndpoint:ApiKeys:1", Named("Workstation")));

        // Act
        var error = Assert.Single(discovered.FindDeclarationErrors());

        // Assert
        Assert.Equal(SecretDeclarationFailure.NameDuplicated, error.Failure);
    }

    [Fact]
    public void FindDeclarationErrors_ASecretNamingNoLifetime_AcceptsItBecauseTheDefaultIsSpelledOut()
    {
        // Arrange
        var secret = Named("primary-password");

        // Act
        var errors = Discover(("Persistence:Password", secret)).FindDeclarationErrors();

        // Assert
        Assert.Empty(errors);
        Assert.Equal(SecretLifetime.NoLimitValue, secret.Lifetime);
    }

    [Theory]
    [InlineData("", SecretDeclarationFailure.LifetimeMissing)]
    [InlineData("   ", SecretDeclarationFailure.LifetimeMissing)]
    [InlineData("forever", SecretDeclarationFailure.LifetimeMalformed)]
    [InlineData("2027-01-31", SecretDeclarationFailure.LifetimeMalformed)]
    public void FindDeclarationErrors_AnUnusableLifetime_ReportsWhyRatherThanFallingBackToNoLimit(
        string configuredLifetime,
        SecretDeclarationFailure expectedFailure)
    {
        // Arrange
        var secret = Named("primary-password");
        secret.Lifetime = configuredLifetime;

        // Act
        var error = Assert.Single(Discover(("Persistence:Password", secret)).FindDeclarationErrors());

        // Assert
        Assert.Equal(expectedFailure, error.Failure);
    }

    [Fact]
    public void FindDeclarationErrors_ASecretFaultyInBothNameAndLifetime_ReportsBoth()
    {
        // Arrange
        var secret = new ConfiguredSecret { Lifetime = "forever" };

        // Act
        var errors = Discover(("Persistence:Password", secret)).FindDeclarationErrors();

        // Assert
        Assert.Equal(
            [SecretDeclarationFailure.NameMissing, SecretDeclarationFailure.LifetimeMalformed],
            errors.Select(error => error.Failure));
    }

    /// <summary>A bundle password is a secret of its own, so it declares its own identity rather than borrowing the block's that carries it.</summary>
    [Fact]
    public void FindDeclarationErrors_ANestedBundlePasswordWithNoName_IsReportedLikeAnyOtherSecret()
    {
        // Arrange
        var anchor = Named("primary-ca");
        anchor.Password = new ConfiguredSecret();
        var options = new OptionsUnderTest { TrustedCertificateAuthority = anchor };

        // Act
        var errors = ConfiguredSecretDiscovery
            .FindSecretBearingSettings(options, "MailSynchronization")
            .FindDeclarationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal("MailSynchronization:TrustedCertificateAuthority:Password", error.ConfigurationPath);
        Assert.Equal(SecretDeclarationFailure.NameMissing, error.Failure);
    }

    private static ConfiguredSecret Named(string name) => new()
    {
        Name = name,
        SecretReference = "plaintext:material",
    };

    private static DiscoveredSecretSettings Discover(params (string ConfigurationPath, ConfiguredSecret Secret)[] blocks) =>
        new(
            [.. blocks.Select(block => new DiscoveredSecret(block.ConfigurationPath, block.Secret))],
            RawSecretPropertyPaths: []);

    private sealed class OptionsUnderTest
    {
        public ConfiguredSecret? TrustedCertificateAuthority { get; set; }
    }
}
