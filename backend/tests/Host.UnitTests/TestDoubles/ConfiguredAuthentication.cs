// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the administrators and authentication entries an endpoint section is configured with.</summary>
/// <remarks>
/// An entry carries its method's whole block, so arranging one by hand is several statements before a test says anything
/// about the behavior it covers. Most tests need only a usable one, which is what these produce.
/// <para>
/// The two shapes are not interchangeable and are deliberately built by separate methods. The administrative endpoint
/// names the deployment's own administrators, so each carries its credentials itself; a mail-serving endpoint states
/// which methods it accepts and nothing about who holds one, because a user-facing credential is a row beside the
/// user it resolves.
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

    /// <summary>An administrator holding the given credentials.</summary>
    /// <param name="name">The name every act of the administrator is attributed to.</param>
    /// <param name="credentials">The credentials the administrator may present.</param>
    /// <returns>The administrator, granted nothing until a test writes its permissions.</returns>
    internal static AdministratorOptions Administrator(string name, params AdministratorCredentialOptions[] credentials)
    {
        var administrator = new AdministratorOptions { Name = name };

        foreach (var credential in credentials)
        {
            administrator.Credentials.Add(credential);
        }

        return administrator;
    }

    /// <summary>An administrator named after the one API key it holds.</summary>
    /// <param name="keyName">The name of the key, and of the administrator.</param>
    /// <returns>The administrator.</returns>
    internal static AdministratorOptions AdministratorWithApiKey(string keyName) =>
        Administrator(keyName, ApiKey(keyName));

    /// <summary>A credential carrying one named API key, with a reference of its own.</summary>
    /// <param name="keyName">The name to provision.</param>
    /// <returns>The credential.</returns>
    internal static AdministratorCredentialOptions ApiKey(string keyName) => new()
    {
        ApiKey = new ConfiguredSecret
        {
            Name = keyName,
            SecretReference = $"systemd-credential:mailfathom-{keyName}-key",
        },
    };

    /// <summary>A credential carrying one named client public key, with a reference of its own.</summary>
    /// <param name="keyName">The name to provision.</param>
    /// <returns>The credential.</returns>
    internal static AdministratorCredentialOptions PublicKey(string keyName) => new()
    {
        PublicKey = new ConfiguredSecret
        {
            Name = keyName,
            SecretReference = $"systemd-credential:mailfathom-{keyName}-public-key",
        },
    };

    /// <summary>A credential accepting tokens one authorization server issued for the given resource to one subject.</summary>
    /// <param name="resource">The canonical resource identifier every token's audience is compared against.</param>
    /// <param name="authorizationServerName">The name diagnostics and scheme names are read by.</param>
    /// <param name="issuer">The issuer compared against a token's <c>iss</c>.</param>
    /// <param name="subject">The subject the token has to name to be this administrator's.</param>
    /// <returns>The credential.</returns>
    internal static AdministratorCredentialOptions OAuthFor(
        string resource,
        string authorizationServerName = "workforce",
        string issuer = "https://sso.example.test/realms/mailfathom",
        string subject = "9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04")
    {
        var authorizationServer = new AuthorizationServerOptions { Name = authorizationServerName, Issuer = issuer };
        authorizationServer.AuthorizedSubjects.Add(subject);

        var oauth = new OAuthValidationOptions { Resource = resource };
        oauth.AuthorizationServers.Add(authorizationServer);

        return new AdministratorCredentialOptions { OAuth = oauth };
    }
}
