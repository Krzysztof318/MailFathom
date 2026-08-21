// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Common.ClientAssertions;
using MailFathom.Host.Security.Transport;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

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

    private const string ClientAssertionScheme = "MailFathomClientAssertion";

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

    /// <summary>An assertion declares its own type, which is what tells it from an access token before either has been verified.</summary>
    [Fact]
    public void SchemeFor_AnAssertionDeclaringItsType_ReachesTheAssertionVerification()
    {
        // Arrange
        var selector = AcceptingBoth();

        // Act, Assert
        Assert.Equal(ClientAssertionScheme, selector.SchemeFor(AnAssertion()));
    }

    /// <summary>
    /// The declared type is read before the issuer, so a credential claiming both never reaches a token validator it
    /// could not satisfy. An assertion carrying a configured issuer is exactly what an attacker would send to try it.
    /// </summary>
    [Fact]
    public void SchemeFor_AnAssertionAlsoNamingAConfiguredIssuer_ReachesTheAssertionVerification()
    {
        // Arrange
        var selector = AcceptingBoth();
        var header = Encode($$"""{"alg":"ES256","typ":"{{ClientAssertion.DeclaredType}}"}""");
        var payload = Encode($$"""{"iss":"{{WorkforceIssuer}}","aud":"urn:mailfathom:admin","jti":"x"}""");

        // Act, Assert
        Assert.Equal(ClientAssertionScheme, selector.SchemeFor($"Bearer {header}.{payload}.signature"));
    }

    /// <summary>A token declaring some other type is not an assertion, so it keeps reaching the validator its issuer names.</summary>
    [Theory]
    [InlineData("JWT")]
    [InlineData("at+jwt")]
    [InlineData(null)]
    public void SchemeFor_ATokenDeclaringAnotherType_ReachesItsIssuersValidator(string? declaredType)
    {
        // Arrange
        var selector = AcceptingBoth();
        var header = declaredType is null ? """{"alg":"RS256"}""" : $$"""{"alg":"RS256","typ":"{{declaredType}}"}""";
        var payload = Encode($$"""{"iss":"{{WorkforceIssuer}}"}""");

        // Act, Assert
        Assert.Equal(WorkforceScheme, selector.SchemeFor($"Bearer {Encode(header)}.{payload}.signature"));
    }

    /// <summary>With assertions turned off nothing routes to a scheme that was never registered, which would forward to nothing.</summary>
    [Fact]
    public void SchemeFor_AnAssertionWhereAssertionsAreNotAccepted_ReachesTheApiKeyComparison()
    {
        // Arrange
        var selector = new CredentialSchemeSelector(
            new Dictionary<string, string>(StringComparer.Ordinal),
            ApiKeyScheme,
            clientAssertionSchemeName: null,
            MetadataScheme);

        // Act, Assert
        Assert.Equal(ApiKeyScheme, selector.SchemeFor(AnAssertion()));
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
            clientAssertionSchemeName: null,
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
            clientAssertionSchemeName: null,
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
        ClientAssertionScheme,
        MetadataScheme);

    private static string TokenIssuedBy(string issuer)
    {
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"iss":"{{issuer}}","sub":"9f2c"}"""));

        return $"Bearer {Encode("""{"alg":"RS256","typ":"JWT"}""")}.{payload}.signature";
    }

    private static string AnAssertion(string? declaredType = ClientAssertion.DeclaredType)
    {
        var header = declaredType is null
            ? """{"alg":"ES256"}"""
            : $$"""{"alg":"ES256","typ":"{{declaredType}}"}""";

        return $"Bearer {Encode(header)}.{Encode("""{"aud":"urn:mailfathom:admin","jti":"an-identifier"}""")}.signature";
    }

    private static string Encode(string document) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(document));
}
