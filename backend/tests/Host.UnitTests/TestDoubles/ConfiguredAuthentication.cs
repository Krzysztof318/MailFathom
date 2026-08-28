// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the authentication entries an endpoint section is configured with.</summary>
/// <remarks>
/// An entry carries its method's whole block, so arranging one by hand is several statements before a test says anything
/// about the behavior it covers. Most tests need only a usable one, which is what these produce.
/// <para>
/// The two shapes are not interchangeable and are deliberately built by separate methods. The administrative endpoint
/// configures the deployment's own credentials, so its entry carries the key itself; a mail-serving endpoint states
/// which methods it accepts and nothing about who holds one, because an owner-facing credential is a row beside the
/// owner it resolves.
/// </para>
/// </remarks>
internal static class ConfiguredAuthentication
{
    /// <summary>An owner-facing entry accepting one method, which is the whole of what such an entry states.</summary>
    /// <param name="method">The method the endpoint accepts.</param>
    /// <returns>The entry.</returns>
    internal static OwnerFacingAuthenticationOptions Accepting(OwnerCredentialMethod method) =>
        new() { Method = method.Name };

    /// <summary>An owner-facing entry accepting subjects one authorization server issued for the given resource.</summary>
    /// <param name="resource">The canonical resource identifier every token's audience is compared against.</param>
    /// <param name="authorizationServerName">The name diagnostics and scheme names are read by.</param>
    /// <param name="issuer">The issuer compared against a token's <c>iss</c>.</param>
    /// <returns>The entry.</returns>
    internal static OwnerFacingAuthenticationOptions AcceptingSubjectsFrom(
        string resource,
        string authorizationServerName = "workforce",
        string issuer = "https://sso.example.test/realms/mailfathom")
    {
        var oauth = new OAuthValidationOptions { Resource = resource };
        oauth.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = authorizationServerName,
            Issuer = issuer,
        });

        return new OwnerFacingAuthenticationOptions
        {
            Method = OwnerCredentialMethod.OAuthSubject.Name,
            OAuth = oauth,
        };
    }

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
