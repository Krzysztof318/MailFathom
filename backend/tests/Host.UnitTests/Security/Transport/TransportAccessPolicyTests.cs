// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Basic;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.OAuth;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers what an authenticated caller must satisfy before a route serves it.</summary>
/// <remarks>
/// Every caller acts for a user, so a principal naming none is refused whatever else it carries. Beyond that a token is
/// asked what its issuer requires of it, and a credential this deployment holds a row for is not: the row is the
/// authorization, and nothing could ever put a scope in a key or a password.
/// </remarks>
public sealed class TransportAccessPolicyTests
{
    private const string OAuthScheme = "MailFathomOAuth:workforce";

    private const string Issuer = "https://sso.example.test/realms/mailfathom";

    private static readonly UserId User = UserId.Create(Guid.Parse("3c9a5e1f-2b4d-4c6e-8f0a-1b2c3d4e5f6a"));

    [Fact]
    public void IsUserAuthorized_AnAnonymousCaller_IsRefused()
    {
        // Arrange
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsUserAuthorized(anonymous, ScopesRequiredBy()));
    }

    /// <summary>A caller acts for a user, so an authenticated principal naming nobody is refused rather than served as unrestricted.</summary>
    [Fact]
    public void IsUserAuthorized_AnAuthenticatedPrincipalNamingNoUser_IsRefused()
    {
        // Arrange
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ApiKeyAuthentication.ApiKeyNameClaimType, "nightly-digest")],
            TransportSurface.Mcp.ApiKeySchemeName));

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsUserAuthorized(caller, ScopesRequiredBy()));
    }

    /// <summary>A key carries no scope and nothing could put one in it, so requiring one would refuse every non-interactive client.</summary>
    [Fact]
    public void IsUserAuthorized_AnApiKeyWhereScopesAreRequired_IsAllowedBecauseAKeyCannotCarryOne()
    {
        // Arrange
        var caller = PrincipalCarrying(ApiKeyAuthentication.ApiKeyNameClaimType, "nightly-digest", TransportSurface.Mcp.ApiKeySchemeName);

        // Act, Assert
        Assert.True(TransportAccessPolicy.IsUserAuthorized(caller, ScopesRequiredBy("mailfathom.read")));
    }

    [Fact]
    public void IsUserAuthorized_APasswordWhereScopesAreRequired_IsAllowed()
    {
        // Arrange
        var caller = PrincipalCarrying(
            BasicAuthentication.CredentialIdClaimType,
            "0197c0de-0000-7000-8000-000000000001",
            TransportSurface.Admin.BasicSchemeName);

        // Act, Assert
        Assert.True(TransportAccessPolicy.IsUserAuthorized(caller, ScopesRequiredBy("mailfathom.read")));
    }

    [Fact]
    public void IsUserAuthorized_ATokenCarryingEveryScopeItsIssuerRequires_IsAllowed()
    {
        // Arrange
        var caller = TokenPrincipal("mailfathom.read", "mailfathom.search");

        // Act, Assert
        Assert.True(TransportAccessPolicy.IsUserAuthorized(caller, ScopesRequiredBy("mailfathom.read")));
    }

    [Fact]
    public void IsUserAuthorized_ATokenMissingAScopeItsIssuerRequires_IsRefused()
    {
        // Arrange
        var caller = TokenPrincipal("mailfathom.read");

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsUserAuthorized(caller, ScopesRequiredBy("mailfathom.search")));
    }

    /// <summary>The bypass follows what the principal carries rather than which scheme named it, so a token cannot claim it by naming a scheme.</summary>
    [Fact]
    public void IsUserAuthorized_ATokenAuthenticatedUnderTheApiKeySchemeName_StillHasItsScopesChecked()
    {
        // Arrange
        var identity = OAuthIdentity.FromValidatedToken([new Claim("iss", Issuer), new Claim("sub", "9f2c")], "MailFathomApiKey")!;
        identity.AddClaim(TransportCallerUser.ClaimFor(User));

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsUserAuthorized(new ClaimsPrincipal(identity), ScopesRequiredBy("mailfathom.read")));
    }

    private static Dictionary<string, IReadOnlyCollection<string>> ScopesRequiredBy(params string[] scopes) =>
        new(StringComparer.Ordinal) { [Issuer] = scopes };

    private static ClaimsPrincipal TokenPrincipal(params string[] scopes)
    {
        var identity = OAuthIdentity.FromValidatedToken(
            [new Claim("iss", Issuer), new Claim("sub", "9f2c"), new Claim("scope", string.Join(' ', scopes))],
            OAuthScheme)!;
        identity.AddClaim(TransportCallerUser.ClaimFor(User));

        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal PrincipalCarrying(string claimType, string value, string scheme) => new(
        new ClaimsIdentity([new Claim(claimType, value), TransportCallerUser.ClaimFor(User)], scheme));
}
