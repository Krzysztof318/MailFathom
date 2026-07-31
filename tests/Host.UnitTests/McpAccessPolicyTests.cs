// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Claims;
using MailMcp.Host.Security;
using MailMcp.Infrastructure.Security;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers what an authenticated caller must satisfy before a tool runs.</summary>
/// <remarks>
/// The endpoint asks one question today — is this a caller the deployment recognizes — so most of what is worth stating
/// here is which credentials count as recognized, and that a required scope constrains a token without ever being asked
/// of a key that could not carry one.
/// </remarks>
public sealed class McpAccessPolicyTests
{
    private const string OAuthScheme = "MailMcpOAuth:workforce";

    private const string Issuer = "https://sso.example.test/realms/mailmcp";

    [Fact]
    public void IsAuthorized_AnAnonymousCaller_IsRefused()
    {
        // Arrange
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        // Act, Assert
        Assert.False(McpAccessPolicy.IsAuthorized(anonymous, []));
    }

    [Fact]
    public void IsAuthorized_AValidTokenWhereNoScopeIsRequired_IsAllowed()
    {
        // Arrange
        var caller = TokenPrincipal();

        // Act, Assert
        Assert.True(McpAccessPolicy.IsAuthorized(caller, []));
    }

    [Fact]
    public void IsAuthorized_ATokenCarryingEveryRequiredScope_IsAllowed()
    {
        // Arrange
        var caller = TokenPrincipal("mailmcp.read", "mailmcp.search");

        // Act, Assert
        Assert.True(McpAccessPolicy.IsAuthorized(caller, ["mailmcp.read"]));
    }

    [Fact]
    public void IsAuthorized_ATokenMissingARequiredScope_IsRefused()
    {
        // Arrange
        var caller = TokenPrincipal("mailmcp.read");

        // Act, Assert
        Assert.False(McpAccessPolicy.IsAuthorized(caller, ["mailmcp.search"]));
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
        Assert.True(McpAccessPolicy.IsAuthorized(caller, ["mailmcp.read"]));
    }

    /// <summary>The bypass follows what the principal carries rather than which scheme named it, so a token cannot claim it by naming a scheme.</summary>
    [Fact]
    public void IsAuthorized_ATokenAuthenticatedUnderTheApiKeySchemeName_StillHasItsScopesChecked()
    {
        // Arrange
        var claims = new[] { new Claim("iss", Issuer), new Claim("sub", "9f2c") };
        var identity = McpOAuthIdentity.FromValidatedToken(claims, "MailMcpApiKey");
        var caller = new ClaimsPrincipal(identity!);

        // Act, Assert
        Assert.False(McpAccessPolicy.IsAuthorized(caller, ["mailmcp.read"]));
    }

    private static ClaimsPrincipal TokenPrincipal(params string[] scopes)
    {
        Claim[] claims =
        [
            new("iss", Issuer),
            new("sub", "9f2c"),
            new("scope", string.Join(' ', scopes)),
        ];

        return new ClaimsPrincipal(McpOAuthIdentity.FromValidatedToken(claims, OAuthScheme)!);
    }

    private static ClaimsPrincipal ApiKeyPrincipal(string keyName) => new(
        new ClaimsIdentity(
            [new Claim(McpApiKeyAuthentication.ApiKeyNameClaimType, keyName)],
            McpApiKeyAuthentication.SchemeName,
            McpApiKeyAuthentication.ApiKeyNameClaimType,
            roleType: string.Empty));
}
