// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the authentication entries an endpoint section is configured with.</summary>
/// <remarks>
/// An entry carries its method's whole block, so arranging one by hand is several statements before a test says anything
/// about the behavior it covers. Both endpoints take the same entries, and most tests need only a usable one, which is
/// what these two produce.
/// </remarks>
internal static class ConfiguredAuthentication
{
    /// <summary>An entry accepting one named API key, with a reference of its own.</summary>
    /// <param name="keyName">The name to provision.</param>
    /// <returns>The entry.</returns>
    internal static TransportAuthenticationOptions ApiKey(string keyName) => new()
    {
        ApiKey = new ConfiguredSecret
        {
            Name = keyName,
            SecretReference = $"systemd-credential:mailfathom-{keyName}-key",
        },
    };

    /// <summary>An entry accepting tokens one authorization server issued for the given resource.</summary>
    /// <param name="resource">The canonical resource identifier every token's audience is compared against.</param>
    /// <param name="authorizationServerName">The name diagnostics and scheme names are read by.</param>
    /// <param name="issuer">The issuer compared against a token's <c>iss</c>.</param>
    /// <returns>The entry.</returns>
    internal static TransportAuthenticationOptions OAuthFor(
        string resource,
        string authorizationServerName = "workforce",
        string issuer = "https://sso.example.test/realms/mailfathom")
    {
        var authorizationServer = new AuthorizationServerOptions { Name = authorizationServerName, Issuer = issuer };
        authorizationServer.AuthorizedSubjects.Add("9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04");

        var oauth = new OAuthValidationOptions { Resource = resource };
        oauth.AuthorizationServers.Add(authorizationServer);

        return new TransportAuthenticationOptions { OAuth = oauth };
    }
}
