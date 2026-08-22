// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>That discovery looks where the two specifications actually place a document, and nowhere else.</summary>
public sealed class OAuthMetadataAddressesTests
{
    [Fact]
    public void ForIssuer_AnIssuerWithNoPath_YieldsTheTwoAddressesTheSpecificationsAgreeOn()
    {
        // Arrange, Act
        var candidates = OAuthMetadataAddresses.ForIssuer("https://issuer.example");

        // Assert
        Assert.Equal(
            [
                "https://issuer.example/.well-known/oauth-authorization-server",
                "https://issuer.example/.well-known/openid-configuration",
            ],
            candidates);
    }

    [Fact]
    public void ForIssuer_ATrailingSlash_IsNotAPathAndProducesNoDoubledSeparator()
    {
        // Arrange, Act
        var candidates = OAuthMetadataAddresses.ForIssuer("https://issuer.example/");

        // Assert
        Assert.Equal(
            [
                "https://issuer.example/.well-known/oauth-authorization-server",
                "https://issuer.example/.well-known/openid-configuration",
            ],
            candidates);
    }

    [Fact]
    public void ForIssuer_AnIssuerWithAPath_YieldsBothInsertionFormsAndTheAppendedOne()
    {
        // Arrange, Act
        var candidates = OAuthMetadataAddresses.ForIssuer("https://issuer.example/tenants/mail");

        // Assert
        Assert.Equal(
            [
                "https://issuer.example/.well-known/oauth-authorization-server/tenants/mail",
                "https://issuer.example/.well-known/openid-configuration/tenants/mail",
                "https://issuer.example/tenants/mail/.well-known/openid-configuration",
            ],
            candidates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-issuer")]
    [InlineData("http://issuer.example")]
    [InlineData("https://issuer.example?tenant=mail")]
    [InlineData("https://issuer.example#mail")]
    public void ForIssuer_SomethingThatIsNotAnIssuerIdentifier_ReachesNothing(string? issuer)
    {
        // Arrange, Act
        var candidates = OAuthMetadataAddresses.ForIssuer(issuer);

        // Assert
        Assert.Empty(candidates);
    }
}
