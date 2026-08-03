// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Host.Security;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers which handler judges a presented credential when several kinds are accepted at once.</summary>
/// <remarks>
/// The selection reads an issuer nothing has verified, so what matters is the size of what it decides. Every case below
/// asserts that an attacker writing whatever they like into that field picks which handler refuses them and nothing
/// more — never a handler that is more permissive, and never no handler at all.
/// </remarks>
public sealed class CredentialSchemeSelectorTests
{
    private const string WorkforceIssuer = "https://sso.example.test/realms/mailfathom";

    private const string WorkforceScheme = "MailFathomOAuth:workforce";

    private const string PartnerIssuer = "https://partners.example.test";

    private const string PartnerScheme = "MailFathomOAuth:partners";

    private const string ApiKeyScheme = "MailFathomApiKey";

    private const string MetadataScheme = "McpAuth";

    [Fact]
    public void SchemeFor_ATokenNamingAConfiguredIssuer_ReachesThatIssuersValidator()
    {
        // Arrange
        var selector = AcceptingBoth();

        // Act, Assert
        Assert.Equal(WorkforceScheme, selector.SchemeFor(TokenIssuedBy(WorkforceIssuer)));
    }

    /// <summary>Two configured servers stay isolated, so the issuer a token names decides which key set it is checked against.</summary>
    [Fact]
    public void SchemeFor_TokensFromTwoConfiguredIssuers_ReachTheirOwnValidators()
    {
        // Arrange
        var selector = AcceptingBoth();

        // Act
        var workforceScheme = selector.SchemeFor(TokenIssuedBy(WorkforceIssuer));
        var partnerScheme = selector.SchemeFor(TokenIssuedBy(PartnerIssuer));

        // Assert
        Assert.Equal(WorkforceScheme, workforceScheme);
        Assert.Equal(PartnerScheme, partnerScheme);
    }

    /// <summary>An issuer nobody configured selects no validator, so it can never reach one that would trust a different key set.</summary>
    [Fact]
    public void SchemeFor_ATokenNamingAnIssuerNobodyConfigured_ReachesNoValidator()
    {
        // Arrange
        var selector = AcceptingBoth();

        // Act
        var scheme = selector.SchemeFor(TokenIssuedBy("https://attacker.example.test"));

        // Assert
        Assert.NotEqual(WorkforceScheme, scheme);
        Assert.NotEqual(PartnerScheme, scheme);
    }

    /// <summary>An issuer is compared exactly, because one differing by a trailing slash is a different issuer to every server that emits it.</summary>
    [Fact]
    public void SchemeFor_ATokenNamingAConfiguredIssuerWithATrailingSlash_ReachesNoValidator()
    {
        // Arrange
        var selector = AcceptingBoth();

        // Act
        var scheme = selector.SchemeFor(TokenIssuedBy(WorkforceIssuer + "/"));

        // Assert
        Assert.NotEqual(WorkforceScheme, scheme);
    }

    [Theory]
    [InlineData("Bearer an-opaque-api-key")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("")]
    [InlineData(null)]
    public void SchemeFor_ACredentialThatIsNotAToken_ReachesTheApiKeyComparison(string? headerValue)
    {
        // Arrange
        var selector = AcceptingBoth();

        // Act, Assert
        Assert.Equal(ApiKeyScheme, selector.SchemeFor(headerValue));
    }

    /// <summary>With API keys turned off there is nothing to fall back to, so an unrecognized credential reaches the scheme that answers with the challenge.</summary>
    [Theory]
    [InlineData("Bearer an-opaque-api-key")]
    [InlineData("")]
    public void SchemeFor_AnUnrecognizedCredentialWhereOnlyOAuthIsAccepted_ReachesTheChallenge(string headerValue)
    {
        // Arrange
        var selector = new CredentialSchemeSelector(
            new Dictionary<string, string> { [WorkforceIssuer] = WorkforceScheme },
            apiKeySchemeName: null,
            MetadataScheme);

        // Act, Assert
        Assert.Equal(MetadataScheme, selector.SchemeFor(headerValue));
    }

    /// <summary>With OAuth turned off, something shaped like a token is simply an API key that will not match, and is compared as one.</summary>
    [Fact]
    public void SchemeFor_ATokenWhereOnlyApiKeysAreAccepted_ReachesTheApiKeyComparison()
    {
        // Arrange
        var selector = new CredentialSchemeSelector(
            new Dictionary<string, string>(StringComparer.Ordinal),
            ApiKeyScheme,
            ApiKeyScheme);

        // Act, Assert
        Assert.Equal(ApiKeyScheme, selector.SchemeFor(TokenIssuedBy(WorkforceIssuer)));
    }

    /// <summary>Selection is deterministic, so the same request never reaches two different handlers across two attempts.</summary>
    [Fact]
    public void SchemeFor_TheSameCredentialTwice_ReachesTheSameHandler()
    {
        // Arrange
        var selector = AcceptingBoth();
        var credential = TokenIssuedBy(WorkforceIssuer);

        // Act, Assert
        Assert.Equal(selector.SchemeFor(credential), selector.SchemeFor(credential));
    }

    private static CredentialSchemeSelector AcceptingBoth() => new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkforceIssuer] = WorkforceScheme,
            [PartnerIssuer] = PartnerScheme,
        },
        ApiKeyScheme,
        MetadataScheme);

    private static string TokenIssuedBy(string issuer)
    {
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"iss":"{{issuer}}","sub":"9f2c"}"""));

        return $"Bearer header.{payload}.signature";
    }
}
