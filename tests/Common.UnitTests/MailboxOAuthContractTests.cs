// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Common.MailboxOAuth;
using Xunit;

namespace MailFathom.Common.UnitTests;

/// <summary>Covers the parts of the mailbox authorization exchange that are decided without a server.</summary>
public sealed class MailboxOAuthContractTests
{
    /// <summary>
    /// The token response carries a bearer token and, on an interactive authorization, the refresh token the whole
    /// exchange exists to produce. A default record rendering would put both into any log line that interpolated it.
    /// </summary>
    [Fact]
    public void ToString_ATokenResponse_RedactsTheCredentialsItCarries()
    {
        // Arrange
        var response = new MailOAuthTokenResponse(
            "an-access-token",
            ExpiresInSeconds: 3600,
            Error: null,
            RefreshToken: "a-refresh-token");

        // Act
        var rendered = response.ToString();

        // Assert
        Assert.Equal("***", rendered);
        Assert.DoesNotContain("an-access-token", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("a-refresh-token", rendered, StringComparison.Ordinal);
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
            .ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(proofKey.Verifier)))
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

    /// <summary>
    /// The value comes from a machine this process does not own and RFC 6749 bounds neither its length nor its
    /// content, so a server that has been replaced or misconfigured must not be able to inject a second-looking log
    /// record through an exception message.
    /// </summary>
    [Theory]
    [InlineData("invalid_grant", "invalid_grant")]
    [InlineData("  invalid_client  ", "invalid_client")]
    [InlineData("bad\ncode: forged log line", "badcode:forgedlogline")]
    [InlineData("drop\r\ntable", "droptable")]
    [InlineData("", "unspecified")]
    [InlineData(null, "unspecified")]
    [InlineData("\u0000\u0007", "unspecified")]
    public void Sanitize_ServerSuppliedErrorCode_KeepsOnlyPrintableSingleLineText(string? serverValue, string expected)
    {
        // Arrange, Act
        var sanitized = AuthorizationServerErrorText.Sanitize(serverValue);

        // Assert
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public void Sanitize_UnboundedErrorCode_IsTruncatedSoALogLineStaysALogLine()
    {
        // Arrange
        var serverValue = new string('a', 4096);

        // Act
        var sanitized = AuthorizationServerErrorText.Sanitize(serverValue);

        // Assert
        Assert.Equal(64, sanitized.Length);
    }
}
