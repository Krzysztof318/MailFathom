// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers what this deployment is called in OAuth terms, and what it refuses to be called.</summary>
public sealed class OAuthValidationOptionsTests
{
    [Fact]
    public void CanonicalResource_AResourceSpelledUnusually_IsBroughtToTheFormTokensAreComparedAgainst()
    {
        // Arrange
        var oauth = new OAuthValidationOptions { Resource = "HTTPS://Mail.Example.Test:443/mcp" };

        // Act, Assert
        Assert.Equal("https://mail.example.test/mcp", oauth.CanonicalResource());
    }

    [Fact]
    public void CanonicalResource_AResourceThatWasNeverValidated_ThrowsRatherThanYieldingSomethingToCompareAgainst()
    {
        // Arrange
        var oauth = new OAuthValidationOptions { Resource = "not-a-url" };

        // Act, Assert
        Assert.Throws<InvalidOperationException>(oauth.CanonicalResource);
    }

    [Fact]
    public void IsConfigured_AnUntouchedSection_ReportsNothingWasWritten()
    {
        // Arrange, Act
        var oauth = new OAuthValidationOptions();

        // Assert
        Assert.False(oauth.IsConfigured);
    }

    /// <summary>A half-written section still counts as written, so it is reported rather than treated as absent by a deployment that meant to turn OAuth on.</summary>
    [Fact]
    public void IsConfigured_AResourceAlone_ReportsThatSomethingWasWritten()
    {
        // Arrange, Act
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };

        // Assert
        Assert.True(oauth.IsConfigured);
    }

    [Fact]
    public void FindConfigurationErrors_ARepeatedRequiredScope_IsRefused()
    {
        // Arrange
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };
        oauth.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
            AuthorizedSubjects = { "9f2c" },
        });
        oauth.RequiredScopes.Add("mailfathom.read");
        oauth.RequiredScopes.Add("mailfathom.read");

        // Act
        var error = Assert.Single(oauth.FindConfigurationErrors(OAuthSubjectAdmission.ConfiguredSubjects));

        // Assert
        Assert.StartsWith("RequiredScopes:1", error, StringComparison.Ordinal);
    }

    /// <summary>An advertised scope reaches the metadata document and a challenge exactly as a required one does, so a value that is not a scope token is refused there too.</summary>
    [Fact]
    public void FindConfigurationErrors_AnAdvertisedScopeThatIsNotAScopeToken_IsRefusedNamingItsIndex()
    {
        // Arrange
        var oauth = ConfiguredEntry();
        oauth.AdvertisedScopes.Add("offline_access");
        oauth.AdvertisedScopes.Add("two scopes");

        // Act
        var error = Assert.Single(oauth.FindConfigurationErrors(OAuthSubjectAdmission.ConfiguredSubjects));

        // Assert
        Assert.StartsWith("AdvertisedScopes:1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ARepeatedAdvertisedScope_IsRefused()
    {
        // Arrange
        var oauth = ConfiguredEntry();
        oauth.AdvertisedScopes.Add("offline_access");
        oauth.AdvertisedScopes.Add("offline_access");

        // Act
        var error = Assert.Single(oauth.FindConfigurationErrors(OAuthSubjectAdmission.ConfiguredSubjects));

        // Assert
        Assert.StartsWith("AdvertisedScopes:1", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every required scope is published regardless, so repeating one here would state nothing and would leave the
    /// setting reading as the whole advertised set rather than as what is advertised beyond what is checked.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AnAdvertisedScopeThatIsAlreadyRequired_IsRefused()
    {
        // Arrange
        var oauth = ConfiguredEntry();
        oauth.RequiredScopes.Add("mailfathom.read");
        oauth.AdvertisedScopes.Add("mailfathom.read");

        // Act
        var error = Assert.Single(oauth.FindConfigurationErrors(OAuthSubjectAdmission.ConfiguredSubjects));

        // Assert
        Assert.StartsWith("AdvertisedScopes:0", error, StringComparison.Ordinal);
    }

    /// <summary>An entry advertising a scope beside the ones it requires is what the separation is for, and is accepted.</summary>
    [Fact]
    public void FindConfigurationErrors_AnAdvertisedScopeBesideARequiredOne_IsAccepted()
    {
        // Arrange
        var oauth = ConfiguredEntry();
        oauth.RequiredScopes.Add("mailfathom.read");
        oauth.AdvertisedScopes.Add("offline_access");

        // Act, Assert
        Assert.Empty(oauth.FindConfigurationErrors(OAuthSubjectAdmission.ConfiguredSubjects));
    }

    /// <summary>A section carrying nothing but an advertised scope was still written, so it is reported rather than treated as an OAuth block nobody meant to configure.</summary>
    [Fact]
    public void IsConfigured_AnAdvertisedScopeAlone_ReportsThatSomethingWasWritten()
    {
        // Arrange
        var oauth = new OAuthValidationOptions();

        // Act
        oauth.AdvertisedScopes.Add("offline_access");

        // Assert
        Assert.True(oauth.IsConfigured);
    }

    /// <summary>Two profiles sharing a name would leave a diagnostic unable to say which of them it meant.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoAuthorizationServersSharingAName_IsRefused()
    {
        // Arrange
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };
        oauth.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
            AuthorizedSubjects = { "9f2c" },
        });
        oauth.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "Workforce",
            Issuer = "https://partners.example.test",
            AuthorizedSubjects = { "4b81" },
        });

        // Act
        var error = Assert.Single(oauth.FindConfigurationErrors(OAuthSubjectAdmission.ConfiguredSubjects));

        // Assert
        Assert.StartsWith("AuthorizationServers:1:Name", error, StringComparison.Ordinal);
    }

    /// <summary>An entry with nothing wrong with it, so a test asserting one refusal reads that refusal rather than the section's other omissions.</summary>
    private static OAuthValidationOptions ConfiguredEntry()
    {
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };

        oauth.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
            AuthorizedSubjects = { "9f2c" },
        });

        return oauth;
    }
}
