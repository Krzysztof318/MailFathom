// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.OAuth;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.OAuth;

public sealed class MailOAuthContractTests
{
    [Theory]
    [InlineData(" refresh_token ", true)]
    [InlineData("CLIENT_CREDENTIALS", false)]
    public void TryParseGrantTypeName_SupportedName_ParsesTheGrantAndItsRefreshTokenRequirement(
        string configuredName,
        bool expectsRefreshToken)
    {
        // Arrange, Act
        var parsed = MailOAuthGrant.TryParseGrantTypeName(configuredName, out var grant);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expectsRefreshToken, grant.RequiresRefreshToken);
    }

    [Theory]
    [InlineData("authorization_code")]
    [InlineData("password")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseGrantTypeName_GrantAHeadlessProcessCannotComplete_ReturnsFalse(string? configuredName)
    {
        // Arrange, Act
        var parsed = MailOAuthGrant.TryParseGrantTypeName(configuredName, out _);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void GrantTypeName_StructDefault_ThrowsInsteadOfReportingAGrant()
    {
        // Arrange
        MailOAuthGrant unspecifiedGrant = default;

        // Act, Assert
        Assert.False(unspecifiedGrant.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecifiedGrant.GrantTypeName);
    }

    [Fact]
    public void TrySelectAccessTokenMechanism_ServerAdvertisesBoth_PrefersTheRegisteredOAuthBearer()
    {
        // Arrange
        var advertised = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "XOAUTH2", "OAUTHBEARER" };
        var policy = CreatePolicy(MailAuthenticationMechanism.XOAuth2, MailAuthenticationMechanism.OAuthBearer);

        // Act
        var selected = MailKitTransportSecurityMapping.TrySelectAccessTokenMechanism(advertised, policy, out var mechanism);

        // Assert
        Assert.True(selected);
        Assert.Equal(MailAuthenticationMechanism.OAuthBearer, mechanism);
    }

    [Fact]
    public void TrySelectAccessTokenMechanism_ServerAdvertisesOnlyXOAuth2_SelectsIt()
    {
        // Arrange
        var advertised = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "XOAUTH2" };
        var policy = CreatePolicy(MailAuthenticationMechanism.XOAuth2, MailAuthenticationMechanism.OAuthBearer);

        // Act
        var selected = MailKitTransportSecurityMapping.TrySelectAccessTokenMechanism(advertised, policy, out var mechanism);

        // Assert
        Assert.True(selected);
        Assert.Equal(MailAuthenticationMechanism.XOAuth2, mechanism);
    }

    [Fact]
    public void TrySelectAccessTokenMechanism_PolicyPermitsOnlyPasswordMechanisms_SelectsNone()
    {
        // Arrange
        var advertised = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "XOAUTH2", "PLAIN" };
        var policy = CreatePolicy(MailAuthenticationMechanism.Plain);

        // Act
        var selected = MailKitTransportSecurityMapping.TrySelectAccessTokenMechanism(advertised, policy, out var mechanism);

        // Assert
        Assert.False(selected);
        Assert.False(mechanism.IsSpecified);
    }

    [Fact]
    public void TrySelectAccessTokenMechanism_EmptiedAdvertisedSet_SelectsNoneBecauseTheLoginFallbackCarriesAPassword()
    {
        // Arrange: the server advertised no AUTH= capability, which leaves the IMAP LOGIN command as the last resort.
        var advertised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var policy = CreatePolicy(MailAuthenticationMechanism.OAuthBearer, MailAuthenticationMechanism.Plain);

        // Act
        var selected = MailKitTransportSecurityMapping.TrySelectAccessTokenMechanism(advertised, policy, out _);

        // Assert
        Assert.False(selected);
    }

    [Fact]
    public void ToSaslMechanism_PasswordMechanism_ThrowsRatherThanPresentingATokenThroughIt()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MailKitTransportSecurityMapping.ToSaslMechanism(
            MailAuthenticationMechanism.Plain,
            "mailbox@example.test",
            "token"));
    }

    [Theory]
    [InlineData("XOAUTH2")]
    [InlineData("OAUTHBEARER")]
    public void ToSaslMechanism_TokenMechanism_ProducesTheMatchingMailKitContext(string saslName)
    {
        // Arrange
        Assert.True(MailAuthenticationMechanism.TryParseSaslName(saslName, out var mechanism));

        // Act
        var saslMechanism = MailKitTransportSecurityMapping.ToSaslMechanism(mechanism, "mailbox@example.test", "token");

        // Assert
        Assert.Equal(saslName, saslMechanism.MechanismName);
    }

    [Fact]
    public void AuthorizationServerErrorCode_OnTheException_IsTheSanitizedValueTheMessageCarries()
    {
        // Arrange, Act
        var failure = new MailAccessTokenUnavailableException("primary", "invalid_grant\nforged");

        // Assert: the payload a caller reads and the message an operator reads cannot disagree.
        Assert.Equal("invalid_grantforged", failure.AuthorizationServerErrorCode);
        Assert.Contains("invalid_grantforged", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', failure.Message);
    }

    private static MailAuthenticationPolicy CreatePolicy(params MailAuthenticationMechanism[] permittedMechanisms) =>
        MailAuthenticationPolicy.Create(
            permittedMechanisms,
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);
}
