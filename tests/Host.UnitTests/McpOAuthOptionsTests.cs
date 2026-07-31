// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what this deployment is called in OAuth terms and where it says so.</summary>
public sealed class McpOAuthOptionsTests
{
    /// <summary>
    /// The address is composed from the configured resource rather than from the request asking for it. Derived from the
    /// request, a deployment behind a reverse proxy would tell each client to authenticate for whichever name that client
    /// arrived under, including one an attacker chose.
    /// </summary>
    [Theory]
    [InlineData("https://mail.example.test/mcp", "https://mail.example.test/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mail.example.test", "https://mail.example.test/.well-known/oauth-protected-resource")]
    [InlineData("https://mail.example.test/", "https://mail.example.test/.well-known/oauth-protected-resource")]
    [InlineData("https://mail.example.test:8443/mcp", "https://mail.example.test:8443/.well-known/oauth-protected-resource/mcp")]
    public void ProtectedResourceMetadataAddress_AConfiguredResource_PublishesUnderThatResourcesAuthority(
        string resource,
        string expectedAddress)
    {
        // Arrange
        var oauth = new McpOAuthOptions { Resource = resource };

        // Act, Assert
        Assert.Equal(expectedAddress, oauth.ProtectedResourceMetadataAddress());
    }

    [Fact]
    public void CanonicalResource_AResourceSpelledUnusually_IsBroughtToTheFormTokensAreComparedAgainst()
    {
        // Arrange
        var oauth = new McpOAuthOptions { Resource = "HTTPS://Mail.Example.Test:443/mcp" };

        // Act, Assert
        Assert.Equal("https://mail.example.test/mcp", oauth.CanonicalResource());
    }

    [Fact]
    public void CanonicalResource_AResourceThatWasNeverValidated_ThrowsRatherThanYieldingSomethingToCompareAgainst()
    {
        // Arrange
        var oauth = new McpOAuthOptions { Resource = "not-a-url" };

        // Act, Assert
        Assert.Throws<InvalidOperationException>(oauth.CanonicalResource);
    }

    [Fact]
    public void IsConfigured_AnUntouchedSection_ReportsNothingWasWritten()
    {
        // Arrange, Act
        var oauth = new McpOAuthOptions();

        // Assert
        Assert.False(oauth.IsConfigured);
    }

    /// <summary>A half-written section still counts as written, so it is reported rather than treated as absent by a deployment that meant to turn OAuth on.</summary>
    [Fact]
    public void IsConfigured_AResourceAlone_ReportsThatSomethingWasWritten()
    {
        // Arrange, Act
        var oauth = new McpOAuthOptions { Resource = "https://mail.example.test/mcp" };

        // Assert
        Assert.True(oauth.IsConfigured);
    }

    [Fact]
    public void FindConfigurationErrors_ARepeatedRequiredScope_IsRefused()
    {
        // Arrange
        var oauth = new McpOAuthOptions { Resource = "https://mail.example.test/mcp" };
        oauth.AuthorizationServers.Add(new McpAuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
            AuthorizedSubjects = { "9f2c" },
        });
        oauth.RequiredScopes.Add("mailfathom.read");
        oauth.RequiredScopes.Add("mailfathom.read");

        // Act
        var error = Assert.Single(oauth.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("RequiredScopes:1", error, StringComparison.Ordinal);
    }

    /// <summary>Two profiles sharing a name would leave a diagnostic unable to say which of them it meant.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoAuthorizationServersSharingAName_IsRefused()
    {
        // Arrange
        var oauth = new McpOAuthOptions { Resource = "https://mail.example.test/mcp" };
        oauth.AuthorizationServers.Add(new McpAuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
            AuthorizedSubjects = { "9f2c" },
        });
        oauth.AuthorizationServers.Add(new McpAuthorizationServerOptions
        {
            Name = "Workforce",
            Issuer = "https://partners.example.test",
            AuthorizedSubjects = { "4b81" },
        });

        // Act
        var error = Assert.Single(oauth.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("AuthorizationServers:1:Name", error, StringComparison.Ordinal);
    }
}
