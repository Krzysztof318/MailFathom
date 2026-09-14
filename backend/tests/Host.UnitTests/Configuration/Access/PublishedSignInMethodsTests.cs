// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers what a mail-serving endpoint publishes to a browser about to draw a sign-in screen, and which origins that obliges the page's policy to admit.</summary>
public sealed class PublishedSignInMethodsTests
{
    private const string Keycloak = "https://sso.example.test/realms/mailfathom";
    private const string Partner = "https://id.partner.example.test";

    /// <summary>An endpoint accepting no credential takes a request carrying a password exactly as it takes one carrying nothing.</summary>
    [Fact]
    public void For_AnEndpointAcceptingNoCredential_SaysAPasswordMayBePresented()
    {
        // Act
        var published = PublishedSignInMethods.For([]);

        // Assert
        Assert.True(published.AcceptsPassword);
        Assert.Empty(published.AuthorizationServers);
    }

    [Fact]
    public void For_AnEndpointAcceptingAPassword_SaysSo()
    {
        // Act
        var published = PublishedSignInMethods.For([Password()]);

        // Assert
        Assert.True(published.AcceptsPassword);
    }

    /// <summary>A screen drawing a password form against a deployment that refuses one is a form nobody can submit.</summary>
    [Fact]
    public void For_AnEndpointAcceptingTokensAlone_SaysNoPasswordMayBePresented()
    {
        // Act
        var published = PublishedSignInMethods.For([OAuth(Server("keycloak", Keycloak, "mailfathom-client"))]);

        // Assert
        Assert.False(published.AcceptsPassword);
    }

    [Fact]
    public void For_SeveralServers_PublishesEachInConfigurationOrder()
    {
        // Arrange
        var entry = OAuth(
            Server(AuthorizationServerOptions.SelfName, Keycloak, "mailfathom-client"),
            Server("github", Partner, "mailfathom"));

        // Act
        var published = PublishedSignInMethods.For([entry]);

        // Assert
        Assert.Equal(
            [AuthorizationServerOptions.SelfName, "github"],
            published.AuthorizationServers.Select(server => server.Name));
    }

    /// <summary>A deployment serving agents alone publishes no button a browser could not follow.</summary>
    [Fact]
    public void For_AServerNamingNoClientIdentifier_PublishesItToNobody()
    {
        // Arrange
        var entry = OAuth(Server("workforce", Keycloak, clientId: null), Server("github", Partner, "mailfathom"));

        // Act
        var published = PublishedSignInMethods.For([entry]);

        // Assert
        Assert.Equal(["github"], published.AuthorizationServers.Select(server => server.Name));
    }

    [Fact]
    public void For_AServerWritingItsOwnWords_PublishesThemBesideTheNameAMarkIsMatchedOn()
    {
        // Arrange
        var server = Server("keycloak", Keycloak, "mailfathom-client");
        server.DisplayName = "Nordwind staff directory";

        // Act
        var published = Assert.Single(PublishedSignInMethods.For([OAuth(server)]).AuthorizationServers);

        // Assert
        Assert.Equal("keycloak", published.Name);
        Assert.Equal("Nordwind staff directory", published.DisplayName);
        Assert.Equal("mailfathom-client", published.ClientId);
        Assert.Equal(Keycloak, published.Issuer);
    }

    /// <summary>The origin rather than the issuer, because a client reaches a discovery document and a token endpoint and neither path is the issuer's own.</summary>
    [Fact]
    public void IssuerOrigins_AnIssuerCarryingAPath_ReportsTheOriginThePageHasToBePermittedToCall()
    {
        // Act
        var published = PublishedSignInMethods.For([OAuth(Server("keycloak", Keycloak, "mailfathom-client"))]);

        // Assert
        Assert.Equal(["https://sso.example.test"], published.IssuerOrigins());
    }

    /// <summary>Two realms on one server widen the policy once rather than twice.</summary>
    [Fact]
    public void IssuerOrigins_TwoProfilesOnOneServer_WidenThePolicyOnce()
    {
        // Arrange
        var entry = OAuth(
            Server("staff", "https://sso.example.test/realms/staff", "mailfathom-client"),
            Server("partners", "https://sso.example.test/realms/partners", "mailfathom-client"));

        // Act
        var published = PublishedSignInMethods.For([entry]);

        // Assert
        Assert.Equal(["https://sso.example.test"], published.IssuerOrigins());
    }

    [Fact]
    public void IssuerOrigins_AnIssuerOnAPortOfItsOwn_KeepsThePortTheOriginCarries()
    {
        // Act
        var published = PublishedSignInMethods.For([OAuth(Server("keycloak", "https://sso.example.test:8443", "mailfathom-client"))]);

        // Assert
        Assert.Equal(["https://sso.example.test:8443"], published.IssuerOrigins());
    }

    [Fact]
    public void IssuerOrigins_AnEndpointOfferingNoServer_WidensNothing()
    {
        // Act
        var published = PublishedSignInMethods.For([Password()]);

        // Assert
        Assert.Empty(published.IssuerOrigins());
    }

    private static UserFacingAuthenticationOptions Password() =>
        new() { Method = UserCredentialMethod.Password.Name };

    private static UserFacingAuthenticationOptions OAuth(params AuthorizationServerOptions[] servers)
    {
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test" };

        foreach (var server in servers)
        {
            oauth.AuthorizationServers.Add(server);
        }

        return new UserFacingAuthenticationOptions { Method = UserCredentialMethod.OAuthSubject.Name, OAuth = oauth };
    }

    private static AuthorizationServerOptions Server(string name, string issuer, string? clientId) =>
        new() { Name = name, Issuer = issuer, ClientId = clientId };
}
