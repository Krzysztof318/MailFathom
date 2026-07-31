// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Claims;
using MailMcp.Host.Security;
using MailMcp.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the rules a token from a configured authorization server is accepted under.</summary>
/// <remarks>
/// These are the deployment's acceptance rules, and every one of them is a refusal that only shows up when a token is
/// presented — a scheme is configured lazily, so a rule the validator rejects outright would otherwise first be noticed
/// by a request in production.
/// </remarks>
public sealed class McpOAuthAuthenticationTests
{
    private const string Issuer = "https://sso.example.test/realms/mailmcp";

    private const string CanonicalResource = "https://mail.example.test/mcp";

    [Fact]
    public void TokenValidationParametersFor_AnAuthorizationServer_BindsTokensToThatIssuerAndThisResource()
    {
        // Act
        var validationParameters = McpOAuthAuthentication.TokenValidationParametersFor(Issuer, CanonicalResource);

        // Assert
        Assert.True(validationParameters.ValidateIssuer);
        Assert.Equal(Issuer, validationParameters.ValidIssuer);
        Assert.True(validationParameters.ValidateAudience);
        Assert.Equal(CanonicalResource, validationParameters.ValidAudience);
    }

    /// <summary>An unsigned token and a symmetric signature are both refused, because a verification key that can also sign is a key anything holding it can mint tokens with.</summary>
    [Fact]
    public void TokenValidationParametersFor_AnAuthorizationServer_PermitsOnlyAsymmetricSignatures()
    {
        // Act
        var validationParameters = McpOAuthAuthentication.TokenValidationParametersFor(Issuer, CanonicalResource);

        // Assert
        Assert.True(validationParameters.RequireSignedTokens);
        Assert.True(validationParameters.ValidateIssuerSigningKey);
        Assert.NotNull(validationParameters.ValidAlgorithms);
        Assert.DoesNotContain(SecurityAlgorithms.None, validationParameters.ValidAlgorithms);
        Assert.DoesNotContain(SecurityAlgorithms.HmacSha256, validationParameters.ValidAlgorithms);
        Assert.Contains(SecurityAlgorithms.RsaSha256, validationParameters.ValidAlgorithms);
    }

    [Fact]
    public void TokenValidationParametersFor_AnAuthorizationServer_RefusesAnExpiredTokenWithinASkewShorterThanTheFrameworkDefault()
    {
        // Act
        var validationParameters = McpOAuthAuthentication.TokenValidationParametersFor(Issuer, CanonicalResource);

        // Assert
        Assert.True(validationParameters.ValidateLifetime);
        Assert.True(validationParameters.RequireExpirationTime);
        Assert.True(validationParameters.ClockSkew < TokenValidationParameters.DefaultClockSkew);
    }

    /// <summary>
    /// The validator refuses an empty role claim type outright, and the framework's default would let a role claim an
    /// authorization server chose to include answer a check no configuration authorized. Naming a claim type nothing
    /// issues is what satisfies both, so this states the property rather than the spelling.
    /// </summary>
    [Fact]
    public void TokenValidationParametersFor_AnAuthorizationServer_ReadsRolesFromAClaimTypeNothingIssues()
    {
        // Act
        var validationParameters = McpOAuthAuthentication.TokenValidationParametersFor(Issuer, CanonicalResource);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(validationParameters.RoleClaimType));
        Assert.NotEqual(ClaimsIdentity.DefaultRoleClaimType, validationParameters.RoleClaimType);
        Assert.Equal(McpOAuthIdentity.RoleClaimType, validationParameters.RoleClaimType);
    }
}
