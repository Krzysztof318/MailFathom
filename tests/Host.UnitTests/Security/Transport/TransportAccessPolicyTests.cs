// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.OAuth;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers what an authenticated caller must satisfy before a tool runs.</summary>
/// <remarks>
/// The endpoint asks two questions of a token — whose it is, and what it was issued for — so what is worth stating here
/// is that neither substitutes for the other, that a subject is only meaningful together with the issuer that named it,
/// and that neither is ever asked of a key that could not carry one.
/// </remarks>
public sealed class TransportAccessPolicyTests
{
    private const string OAuthScheme = "MailFathomOAuth:workforce";

    private const string Issuer = "https://sso.example.test/realms/mailfathom";

    private const string OwnerSubject = "9f2c";

    private static readonly HashSet<string> AuthorizedOwner =
        [OAuthIdentity.IdentityOf(Issuer, OwnerSubject)];

    /// <summary>The scopes asked of the issuer these principals carry, which is how the policy looks them up.</summary>
    private static Dictionary<string, IReadOnlyCollection<string>> ScopesRequiredOfTheIssuer(params string[] scopes) =>
        new(StringComparer.Ordinal) { [Issuer] = scopes };

    [Fact]
    public void IsAuthorized_AnAnonymousCaller_IsRefused()
    {
        // Arrange
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsAuthorized(anonymous, AuthorizedOwner, ScopesRequiredOfTheIssuer()));
    }

    [Fact]
    public void IsAuthorized_AnAuthorizedSubjectWhereNoScopeIsRequired_IsAllowed()
    {
        // Arrange
        var caller = TokenPrincipal();

        // Act, Assert
        Assert.True(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer()));
    }

    [Fact]
    public void IsAuthorized_AnAuthorizedSubjectCarryingEveryRequiredScope_IsAllowed()
    {
        // Arrange
        var caller = TokenPrincipal("mailfathom.read", "mailfathom.search");

        // Act, Assert
        Assert.True(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer("mailfathom.read")));
    }

    [Fact]
    public void IsAuthorized_AnAuthorizedSubjectMissingARequiredScope_IsRefused()
    {
        // Arrange
        var caller = TokenPrincipal("mailfathom.read");

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer("mailfathom.search")));
    }

    /// <summary>
    /// A tenant holds whoever the operator's identity platform holds, and MailFathom serves one owner's mail to everyone it
    /// admits. A colleague who can obtain a token for this resource is therefore refused by the subject alone, whatever
    /// the authorization server was willing to put in it.
    /// </summary>
    [Fact]
    public void IsAuthorized_AValidTokenNamingAnotherSubjectOfTheSameTenant_IsRefused()
    {
        // Arrange
        var colleague = TokenPrincipalFor(Issuer, "4b81", "mailfathom.read");

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsAuthorized(colleague, AuthorizedOwner, ScopesRequiredOfTheIssuer("mailfathom.read")));
    }

    /// <summary>A subject is unique only within the server that issued it, so the pair is compared rather than the subject alone.</summary>
    [Fact]
    public void IsAuthorized_TheAuthorizedSubjectNamedByAnotherIssuer_IsRefused()
    {
        // Arrange
        var caller = TokenPrincipalFor("https://sso.other.test/realms/mailfathom", OwnerSubject);

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer()));
    }

    /// <summary>
    /// A key is a credential the operator provisioned by writing it into this deployment's configuration, so the
    /// authorization it carries is that decision. Requiring a scope of it would ask a credential for something nothing
    /// can ever put in it, and would turn a configured scope into an outage for every non-interactive client.
    /// </summary>
    [Fact]
    public void IsAuthorized_AnApiKeyWhereScopesAreRequired_IsAllowedBecauseAKeyCannotCarryOne()
    {
        // Arrange
        var caller = ApiKeyPrincipal("nightly-digest");

        // Act, Assert
        Assert.True(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer("mailfathom.read")));
    }

    /// <summary>A key names no subject and is not expected to, so the subject list constrains tokens alone.</summary>
    [Fact]
    public void IsAuthorized_AnApiKeyWhereSubjectsAreAuthorized_IsAllowedBecauseAKeyNamesNone()
    {
        // Arrange
        var caller = ApiKeyPrincipal("nightly-digest");

        // Act, Assert
        Assert.True(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer()));
    }

    /// <summary>The bypass follows what the principal carries rather than which scheme named it, so a token cannot claim it by naming a scheme.</summary>
    [Fact]
    public void IsAuthorized_ATokenAuthenticatedUnderTheApiKeySchemeName_StillHasItsScopesChecked()
    {
        // Arrange
        var claims = new[] { new Claim("iss", Issuer), new Claim("sub", OwnerSubject) };
        var identity = OAuthIdentity.FromValidatedToken(claims, "MailFathomApiKey");
        var caller = new ClaimsPrincipal(identity!);

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer("mailfathom.read")));
    }

    /// <summary>An authenticated principal carrying no identity at all is refused rather than treated as unrestricted.</summary>
    [Fact]
    public void IsAuthorized_AnAuthenticatedPrincipalCarryingNoSubject_IsRefused()
    {
        // Arrange
        var caller = new ClaimsPrincipal(new ClaimsIdentity(claims: [], OAuthScheme));

        // Act, Assert
        Assert.False(TransportAccessPolicy.IsAuthorized(caller, AuthorizedOwner, ScopesRequiredOfTheIssuer()));
    }

    private static ClaimsPrincipal TokenPrincipal(params string[] scopes) =>
        TokenPrincipalFor(Issuer, OwnerSubject, scopes);

    private static ClaimsPrincipal TokenPrincipalFor(string issuer, string subject, params string[] scopes)
    {
        Claim[] claims =
        [
            new("iss", issuer),
            new("sub", subject),
            new("scope", string.Join(' ', scopes)),
        ];

        return new ClaimsPrincipal(OAuthIdentity.FromValidatedToken(claims, OAuthScheme)!);
    }

    private static ClaimsPrincipal ApiKeyPrincipal(string keyName) => new(
        new ClaimsIdentity(
            [new Claim(ApiKeyAuthentication.ApiKeyNameClaimType, keyName)],
            TransportSurface.Mcp.ApiKeySchemeName,
            ApiKeyAuthentication.ApiKeyNameClaimType,
            roleType: string.Empty));
}
