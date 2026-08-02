// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Infrastructure.Security;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Covers what MailFathom keeps of a validated token, and what it deliberately discards.</summary>
/// <remarks>
/// A claim that survives here is a claim something downstream can eventually be tempted to trust, so the tests that
/// assert an absence are as load-bearing as the ones asserting a value.
/// </remarks>
public sealed class McpOAuthIdentityTests
{
    private const string Scheme = "MailFathomOAuth:workforce";

    private const string Issuer = "https://sso.example.test/realms/mailfathom";

    [Fact]
    public void FromValidatedToken_ATokenNamingASubject_CarriesTheIssuerAndSubjectAsOneIdentity()
    {
        // Arrange
        var claims = TokenClaims(("iss", Issuer), ("sub", "9f2c"));

        // Act
        var identity = McpOAuthIdentity.FromValidatedToken(claims, Scheme);

        // Assert
        Assert.NotNull(identity);
        Assert.Equal(Scheme, identity.AuthenticationType);
        Assert.Equal($"{Issuer}|9f2c", identity.FindFirst(McpOAuthIdentity.SubjectClaimType)?.Value);
        Assert.Equal(Issuer, identity.FindFirst(McpOAuthIdentity.IssuerClaimType)?.Value);
    }

    /// <summary>A subject identifier is unique only within the server that issued it, so two servers naming a subject the same way must not merge into one person.</summary>
    [Fact]
    public void FromValidatedToken_TheSameSubjectFromTwoIssuers_ProducesTwoIdentities()
    {
        // Arrange
        var fromWorkforce = TokenClaims(("iss", Issuer), ("sub", "1"));
        var fromPartners = TokenClaims(("iss", "https://partners.example.test"), ("sub", "1"));

        // Act
        var workforceIdentity = McpOAuthIdentity.FromValidatedToken(fromWorkforce, Scheme);
        var partnerIdentity = McpOAuthIdentity.FromValidatedToken(fromPartners, Scheme);

        // Assert
        Assert.NotEqual(
            workforceIdentity?.FindFirst(McpOAuthIdentity.SubjectClaimType)?.Value,
            partnerIdentity?.FindFirst(McpOAuthIdentity.SubjectClaimType)?.Value);
    }

    /// <summary>Everything an authorization server chose to include beyond the three facts MailFathom acts on is dropped, so nothing downstream can start depending on a claim nobody mapped.</summary>
    [Fact]
    public void FromValidatedToken_ATokenCarryingPersonalClaims_KeepsNoneOfThem()
    {
        // Arrange
        var claims = TokenClaims(
            ("iss", Issuer),
            ("sub", "9f2c"),
            ("email", "person@example.test"),
            ("name", "A Person"),
            ("groups", "finance"),
            ("tid", "a-tenant"));

        // Act
        var identity = McpOAuthIdentity.FromValidatedToken(claims, Scheme);

        // Assert
        Assert.NotNull(identity);
        Assert.All(
            identity.Claims,
            claim => Assert.Contains(
                claim.Type,
                new[] { McpOAuthIdentity.SubjectClaimType, McpOAuthIdentity.IssuerClaimType, McpOAuthIdentity.ScopeClaimType },
                StringComparer.Ordinal));
    }

    /// <summary>Nothing maps a token claim onto a role, so a claim named 'role' must not answer a role check no configuration authorized.</summary>
    [Fact]
    public void FromValidatedToken_ATokenCarryingARoleClaim_LeavesEveryRoleCheckUnanswered()
    {
        // Arrange
        var claims = TokenClaims(("iss", Issuer), ("sub", "9f2c"), ("role", "administrator"));

        // Act
        var identity = McpOAuthIdentity.FromValidatedToken(claims, Scheme);

        // Assert
        Assert.False(new ClaimsPrincipal(identity!).IsInRole("administrator"));
    }

    /// <summary>A client credentials grant produces a valid token naming no person, and this endpoint's authorization story is which person is asking.</summary>
    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    public void FromValidatedToken_ATokenMissingHalfOfItsIdentity_AuthorizesNobody(string missingClaimType)
    {
        // Arrange
        var claims = TokenClaims(("iss", Issuer), ("sub", "9f2c"))
            .Where(claim => claim.Type != missingClaimType);

        // Act, Assert
        Assert.Null(McpOAuthIdentity.FromValidatedToken(claims, Scheme));
    }

    /// <summary>Picking either of two would let enumeration order decide who the request is.</summary>
    [Fact]
    public void FromValidatedToken_ATokenCarryingTwoSubjects_AuthorizesNobody()
    {
        // Arrange
        var claims = TokenClaims(("iss", Issuer), ("sub", "9f2c"), ("sub", "other"));

        // Act, Assert
        Assert.Null(McpOAuthIdentity.FromValidatedToken(claims, Scheme));
    }

    /// <summary>Both spellings are in circulation and neither is a provider-specific branch: nothing here asks which server sent the token.</summary>
    [Theory]
    [InlineData("scope")]
    [InlineData("scp")]
    public void FromValidatedToken_ASpaceDelimitedScopeClaim_BecomesOneClaimPerScope(string scopeClaimType)
    {
        // Arrange
        var claims = TokenClaims(("iss", Issuer), ("sub", "9f2c"), (scopeClaimType, "mailfathom.read mailfathom.search"));

        // Act
        var identity = McpOAuthIdentity.FromValidatedToken(claims, Scheme);

        // Assert
        Assert.Equal(
            ["mailfathom.read", "mailfathom.search"],
            identity!.FindAll(McpOAuthIdentity.ScopeClaimType).Select(scope => scope.Value));
    }

    [Fact]
    public void FromValidatedToken_RepeatedScopeClaims_AreReadWithoutDuplication()
    {
        // Arrange
        var claims = TokenClaims(
            ("iss", Issuer),
            ("sub", "9f2c"),
            ("scp", "mailfathom.read"),
            ("scope", "mailfathom.read mailfathom.search"));

        // Act
        var identity = McpOAuthIdentity.FromValidatedToken(claims, Scheme);

        // Assert
        Assert.Equal(
            ["mailfathom.read", "mailfathom.search"],
            identity!.FindAll(McpOAuthIdentity.ScopeClaimType).Select(scope => scope.Value));
    }

    /// <summary>Requiring no scope is the coarser boundary a deployment gets by default, so any authenticated principal satisfies it.</summary>
    [Fact]
    public void CarriesEveryScope_NoScopeRequired_IsSatisfiedByAnyPrincipal()
    {
        // Arrange
        var principal = PrincipalWithScopes();

        // Act, Assert
        Assert.True(McpOAuthIdentity.CarriesEveryScope(principal, []));
    }

    [Fact]
    public void CarriesEveryScope_EveryRequiredScopePresent_IsSatisfied()
    {
        // Arrange
        var principal = PrincipalWithScopes("mailfathom.read", "mailfathom.search");

        // Act, Assert
        Assert.True(McpOAuthIdentity.CarriesEveryScope(principal, ["mailfathom.read"]));
    }

    [Fact]
    public void CarriesEveryScope_OneRequiredScopeMissing_IsNotSatisfied()
    {
        // Arrange
        var principal = PrincipalWithScopes("mailfathom.read");

        // Act, Assert
        Assert.False(McpOAuthIdentity.CarriesEveryScope(principal, ["mailfathom.read", "mailfathom.search"]));
    }

    /// <summary>Scopes are compared exactly, because a server issuing 'MailFathom.Read' has issued a different scope from the one configured.</summary>
    [Fact]
    public void CarriesEveryScope_AScopeDifferingOnlyInCase_IsNotSatisfied()
    {
        // Arrange
        var principal = PrincipalWithScopes("MailFathom.Read");

        // Act, Assert
        Assert.False(McpOAuthIdentity.CarriesEveryScope(principal, ["mailfathom.read"]));
    }

    /// <summary>
    /// An identity given an empty role type silently reverts to the framework's default, which would leave a role check
    /// reading whichever claim an authorization server called a role. The identity therefore names a claim type nothing
    /// maps, so the check answers no because there is nothing to find.
    /// </summary>
    [Fact]
    public void FromValidatedToken_AValidatedToken_ReadsRolesFromAClaimTypeNothingMaps()
    {
        // Arrange
        var claims = TokenClaims([("iss", Issuer), ("sub", "9f2c"), ("roles", "mailbox-administrator")]);

        // Act
        var identity = McpOAuthIdentity.FromValidatedToken(claims, Scheme);

        // Assert
        Assert.NotNull(identity);
        Assert.NotEqual(ClaimsIdentity.DefaultRoleClaimType, identity.RoleClaimType);
        Assert.False(new ClaimsPrincipal(identity).IsInRole("mailbox-administrator"));
    }

    private static IEnumerable<Claim> TokenClaims(params (string Type, string Value)[] claims) =>
        claims.Select(claim => new Claim(claim.Type, claim.Value));

    private static ClaimsPrincipal PrincipalWithScopes(params string[] scopes)
    {
        var claims = TokenClaims([("iss", Issuer), ("sub", "9f2c"), ("scope", string.Join(' ', scopes))]);

        return new ClaimsPrincipal(McpOAuthIdentity.FromValidatedToken(claims, Scheme)!);
    }
}
