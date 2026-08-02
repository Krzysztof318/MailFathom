// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Mail.OAuth.Authorization;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

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
    public void Create_ProofKey_ProducesAnRfc7636VerifierAndItsSha256Challenge()
    {
        // Arrange, Act
        var proofKey = PkceCodeChallenge.Create();

        // Assert: 32 bytes of entropy encode to the 43-character minimum RFC 7636 allows.
        Assert.Equal(43, proofKey.Verifier.Length);
        Assert.All(proofKey.Verifier, character => Assert.True(
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_',
            $"'{character}' is outside the base64url alphabet."));
        Assert.DoesNotContain('=', proofKey.Challenge);

        var expectedChallenge = Convert
            .ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(proofKey.Verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expectedChallenge, proofKey.Challenge);
    }

    [Fact]
    public void Create_TwoProofKeys_DoNotRepeat()
    {
        // Arrange, Act
        var verifiers = Enumerable.Range(0, 16).Select(_ => PkceCodeChallenge.Create().Verifier).ToArray();

        // Assert
        Assert.Equal(verifiers.Length, verifiers.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Google_Preset_OffersNoDeviceFlowBecauseNoMailScopeIsAllowedThrough_It()
    {
        // Arrange, Act
        var preset = MailProviderPreset.Google;

        // Assert
        Assert.Null(preset.DeviceAuthorizationEndpoint);
        Assert.Equal("https://mail.google.com/", preset.Scope);
        Assert.True(preset.RequiresClientSecret);
    }

    [Fact]
    public void Microsoft_Preset_OffersADeviceFlowAndAsksForOfflineAccess()
    {
        // Arrange, Act
        var preset = MailProviderPreset.Microsoft;

        // Assert
        Assert.NotNull(preset.DeviceAuthorizationEndpoint);
        Assert.Contains("IMAP.AccessAsUser.All", preset.Scope, StringComparison.Ordinal);
        // Entra issues no refresh token without it, and a grant without one strands the deployment at the first expiry.
        Assert.Contains("offline_access", preset.Scope, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" GOOGLE ", "google")]
    [InlineData("Microsoft", "microsoft")]
    public void TryParsePresetName_MixedCaseOrPaddedName_ParsesThePreset(string typedName, string expectedPresetName)
    {
        // Arrange, Act
        var parsed = MailProviderPreset.TryParsePresetName(typedName, out var preset);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expectedPresetName, preset.PresetName);
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

    private static MailAuthenticationPolicy CreatePolicy(params MailAuthenticationMechanism[] permittedMechanisms) =>
        MailAuthenticationPolicy.Create(
            permittedMechanisms,
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);
}
