// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
            ("DataEncryption:Keys:0:Material", Named("primary-data-key")),
            ("DataEncryption:Keys:1:Material", Named("secondary-data-key")));

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

    /// <summary>One credential used twice in a section is one credential, so the operator declares it under one name.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoIdenticalDeclarationsSharingAName_ReportNothing()
    {
        // Arrange
        var discovered = Discover(
            ("Chat:Models:0:ApiKey", Referencing("openrouter-api-key", "file:/etc/mailfathom/secrets/openrouter-api-key")),
            ("Chat:Models:1:ApiKey", Referencing("openrouter-api-key", "file:/etc/mailfathom/secrets/openrouter-api-key")));

        // Act
        var errors = discovered.FindDeclarationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A name that identifies two different credentials identifies neither, which is exactly when a rotation instruction stops being actionable.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoSecretsSharingANameAndNamingDifferentReferences_ReportsTheSecondOne()
    {
        // Arrange
        var discovered = Discover(
            ("McpEndpoint:ApiKeys:0", Referencing("workstation", "plaintext:first")),
            ("McpEndpoint:ApiKeys:1", Referencing("workstation", "plaintext:second")));

        // Act
        var error = Assert.Single(discovered.FindDeclarationErrors());

        // Assert
        Assert.Equal("McpEndpoint:ApiKeys:1", error.ConfigurationPath);
        Assert.Equal(SecretDeclarationFailure.NameReusedForAnotherReference, error.Failure);
    }

    /// <summary>One name states one lifetime: two answers to when the credential expires is the ambiguity the rule exists to refuse.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoSecretsSharingANameAndStatingDifferentLifetimes_ReportsTheSecondOne()
    {
        // Arrange
        var repeated = Named("workstation");
        repeated.Lifetime = "2027-01-31T00:00:00Z";

        var discovered = Discover(
            ("McpEndpoint:ApiKeys:0", Named("workstation")),
            ("McpEndpoint:ApiKeys:1", repeated));

        // Act
        var error = Assert.Single(discovered.FindDeclarationErrors());

        // Assert
        Assert.Equal(SecretDeclarationFailure.NameReusedForAnotherLifetime, error.Failure);
    }

    /// <summary>Two spellings of one instant are one lifetime, so neither declaration has to be re-dated to match the other.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoSecretsSharingANameAndSpellingOneInstantTwoWays_ReportNothing()
    {
        // Arrange
        var claimed = Named("workstation");
        claimed.Lifetime = "2027-01-31T00:00:00Z";
        var repeated = Named("workstation");
        repeated.Lifetime = "2027-01-31T01:00:00+01:00";

        // Act
        var errors = Discover(
            ("McpEndpoint:ApiKeys:0", claimed),
            ("McpEndpoint:ApiKeys:1", repeated)).FindDeclarationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A bundle password is part of what a declaration says, so two declarations disagreeing about it are two credentials.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoSecretsSharingANameAndCarryingDifferentPasswords_ReportsTheSecondOne()
    {
        // Arrange
        var claimed = Named("primary-ca");
        claimed.Password = Named("primary-ca-password");
        var repeated = Named("primary-ca");

        // Act
        var errors = Discover(
            ("MailSynchronization:Accounts:0:TrustedCertificateAuthority", claimed),
            ("MailSynchronization:Accounts:1:TrustedCertificateAuthority", repeated)).FindDeclarationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(SecretDeclarationFailure.NameReusedForAnotherPassword, error.Failure);
    }

    /// <summary>Two names differing only in case are one identity to everyone who reads them.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoNamesDifferingOnlyInCase_AreTreatedAsOneName()
    {
        // Arrange
        var discovered = Discover(
            ("McpEndpoint:ApiKeys:0", Referencing("workstation", "plaintext:first")),
            ("McpEndpoint:ApiKeys:1", Referencing("Workstation", "plaintext:second")));

        // Act
        var error = Assert.Single(discovered.FindDeclarationErrors());

        // Assert
        Assert.Equal(SecretDeclarationFailure.NameReusedForAnotherReference, error.Failure);
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

    /// <summary>
    /// A bundle password is a secret of its own, so two declarations naming one are judged against each other where
    /// they disagree rather than as a difference between the blocks that carry them. That is what lets the comparison
    /// above stop at the password's name: the rule reaches the rest of it through the walk, one level down.
    /// </summary>
    [Fact]
    public void FindDeclarationErrors_OnePasswordNameOverTwoDifferentReferences_ReportsItAgainstThePasswordsOwnPath()
    {
        // Arrange
        var first = Named("primary-ca");
        first.Password = Referencing("primary-ca-password", "plaintext:first");
        var second = Named("primary-ca");
        second.Password = Referencing("primary-ca-password", "plaintext:second");

        // Act
        var errors = ConfiguredSecretDiscovery
            .FindSecretBearingSettings(
                new OptionsUnderTest { TrustedCertificateAuthority = first, ClientCertificate = second },
                "MailSynchronization")
            .FindDeclarationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.EndsWith($":{nameof(ConfiguredSecret.Password)}", error.ConfigurationPath, StringComparison.Ordinal);
        Assert.Equal(SecretDeclarationFailure.NameReusedForAnotherReference, error.Failure);
    }

    private static ConfiguredSecret Named(string name) => Referencing(name, "plaintext:material");

    private static ConfiguredSecret Referencing(string name, string secretReference) => new()
    {
        Name = name,
        SecretReference = secretReference,
    };

    private static DiscoveredSecretSettings Discover(params (string ConfigurationPath, ConfiguredSecret Secret)[] blocks) =>
        new(
            [.. blocks.Select(block => new DiscoveredSecret(block.ConfigurationPath, block.Secret))],
            RawSecretPropertyPaths: []);

    private sealed class OptionsUnderTest
    {
        public ConfiguredSecret? TrustedCertificateAuthority { get; set; }

        public ConfiguredSecret? ClientCertificate { get; set; }
    }
}
