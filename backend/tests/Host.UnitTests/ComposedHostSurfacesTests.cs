// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what the composition settles about the client surface that the request pipeline is then told.</summary>
public sealed class ComposedHostSurfacesTests
{
    [Fact]
    public void ClientSignInMethods_AnEndpointThatIsServed_PublishesWhatItAccepts()
    {
        // Arrange
        var client = new ClientEndpointOptions { Enabled = true };
        client.Authentication.Add(new UserFacingAuthenticationOptions { Method = UserCredentialMethod.Password.Name });

        // Act
        var composed = SurfacesFor(client);

        // Assert
        Assert.True(composed.ClientSignInMethods.AcceptsPassword);
    }

    /// <summary>
    /// An unserved endpoint's entries never reached <see cref="ClientEndpointOptions.FindConfigurationErrors" />, so one
    /// of them may be faulty and still pass every startup check. Reading it would throw out of the composition root,
    /// which reaches an operator as a start that failed naming no setting — on a configuration nothing serves.
    /// </summary>
    [Fact]
    public void ClientSignInMethods_AnUnservedEndpointCarryingAnUnvalidatedServer_PublishesNothingRatherThanThrowing()
    {
        // Arrange
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test" };
        oauth.AuthorizationServers.Add(
            new AuthorizationServerOptions { Name = " ", Issuer = "not an issuer", ClientId = "mailfathom-client" });

        var client = new ClientEndpointOptions { Enabled = false };
        client.Authentication.Add(
            new UserFacingAuthenticationOptions { Method = UserCredentialMethod.OAuthSubject.Name, OAuth = oauth });

        // Act
        var composed = SurfacesFor(client);

        // Assert
        Assert.Empty(composed.ClientSignInMethods.AuthorizationServers);
        Assert.Empty(composed.ClientSignInMethods.IssuerOrigins());
    }

    private static ComposedHostSurfaces SurfacesFor(ClientEndpointOptions client) =>
        new(
            new McpEndpointOptions(),
            new AdminEndpointOptions(),
            client,
            new HealthEndpointOptions(),
            ListenerComposition.Compose([]),
            McpRateLimits: null,
            AdminRateLimits: null,
            ClientRateLimits: null,
            IsRateLimited: false,
            McpRequestTimeout: null,
            AdminRequestTimeout: null,
            ClientRequestTimeout: null);
}
