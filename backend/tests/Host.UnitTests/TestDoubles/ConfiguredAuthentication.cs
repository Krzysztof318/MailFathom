// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the authentication entries an endpoint section is configured with.</summary>
/// <remarks>
/// An entry carries its method's whole block, so arranging one by hand is several statements before a test says anything
/// about the behavior it covers. Most tests need only a usable one, which is what these produce.
/// <para>
/// An entry states which methods an endpoint accepts and nothing about who holds one, because a credential is a row
/// beside the user it resolves.
/// </para>
/// </remarks>
internal static class ConfiguredAuthentication
{
    /// <summary>A user-facing entry accepting one method, which is the whole of what such an entry states.</summary>
    /// <param name="method">The method the endpoint accepts.</param>
    /// <returns>The entry.</returns>
    internal static UserFacingAuthenticationOptions Accepting(UserCredentialMethod method) =>
        new() { Method = method.Name };

    /// <summary>A user-facing entry accepting subjects one authorization server issued for the given resource.</summary>
    /// <param name="resource">The canonical resource identifier every token's audience is compared against.</param>
    /// <param name="authorizationServerName">The name diagnostics and scheme names are read by.</param>
    /// <param name="issuer">The issuer compared against a token's <c>iss</c>.</param>
    /// <returns>The entry.</returns>
    internal static UserFacingAuthenticationOptions AcceptingSubjectsFrom(
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

        return new UserFacingAuthenticationOptions
        {
            Method = UserCredentialMethod.OAuthSubject.Name,
            OAuth = oauth,
        };
    }
}
