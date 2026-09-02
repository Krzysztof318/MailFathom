// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Common.ClientAssertions;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ClientAssertions;

/// <summary>Covers what a signed assertion has to be before the endpoint serves the request carrying it.</summary>
/// <remarks>
/// Every refusal below produces one indistinguishable response, so what these tests establish is that each of them is a
/// refusal at all. The ones worth naming are the two a valid signature alone would otherwise pass: an assertion minted
/// for the other surface, and one presented a second time.
/// </remarks>
public sealed class ClientAssertionAuthenticatorTests
{
    private const string KeyName = "nightly-digest";

    private static readonly DateTimeOffset VerifiedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthenticateAsync_AnAssertionSignedByAConfiguredKey_NamesThatKey()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey));

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(KeyName, result.AuthenticatedKeyName?.Value);
    }

    /// <summary>An RSA client is served on exactly the same terms, so the method is not quietly elliptic-curve only.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionSignedByAConfiguredRsaKey_NamesThatKey()
    {
        // Arrange
        using var clientKey = RSA.Create(ClientAssertionKeyMaterial.ShortestRsaModulusInBits);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey));

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(KeyName, result.AuthenticatedKeyName?.Value);
    }

    /// <summary>The deployment holds the public half only, so a client whose key it never registered is refused however correctly it signs.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionSignedByAKeyNobodyRegistered_IsRefused()
    {
        // Arrange
        using var registeredKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var strangersKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(registeredKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(strangersKey));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ClientAssertionRejection.SignatureUnrecognized, result.Rejection);
    }

    /// <summary>The audience is what separates reading a mailbox from administering the service, so an assertion minted for one surface is refused at the other.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionMintedForTheOtherSurface_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(
            Presenting(clientKey, audience: ClientAssertion.McpAudience),
            audience: ClientAssertion.AdminAudience);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ClientAssertionRejection.ClaimsUnacceptable, result.Rejection);
    }

    /// <summary>Serving the same assertion twice is the replay a short lifetime alone cannot refuse, so the second presentation is the one that has to fail.</summary>
    [Fact]
    public async Task AuthenticateAsync_TheSameAssertionTwice_RefusesTheSecondPresentation()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);
        var assertion = Presenting(clientKey);

        // Act
        var first = await harness.AuthenticateAsync(assertion);
        var second = await harness.AuthenticateAsync(assertion);

        // Assert
        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ClientAssertionRejection.IdentifierAlreadySpent, second.Rejection);
    }

    /// <summary>Two assertions differing only in their identifier are two credentials, so refusing a replay must not refuse the client's next request.</summary>
    [Fact]
    public async Task AuthenticateAsync_TwoAssertionsFromOneClient_ServeBoth()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var first = await harness.AuthenticateAsync(Presenting(clientKey, identifier: "first"));
        var second = await harness.AuthenticateAsync(Presenting(clientKey, identifier: "second"));

        // Assert
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    /// <summary>A credential minted a day ahead is refused, which is what makes the short lifetime a fact rather than a convention the client observes.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionClaimingALongerLifeThanPermitted_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(
            Presenting(clientKey, expiresAt: VerifiedAt + TimeSpan.FromDays(1)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ClientAssertionRejection.ClaimsUnacceptable, result.Rejection);
    }

    [Fact]
    public async Task AuthenticateAsync_AnAssertionThatHasExpired_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(
            Presenting(clientKey, expiresAt: VerifiedAt - TimeSpan.FromHours(1)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ClientAssertionRejection.ClaimsUnacceptable, result.Rejection);
    }

    /// <summary>The identifier is what the endpoint remembers, so one no assertion could reasonably carry is refused before it is stored.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionCarryingAnOverlongIdentifier_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(
            Presenting(clientKey, identifier: new string('x', ClientAssertion.IdentifierLengthLimit + 1)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ClientAssertionRejection.ClaimsUnacceptable, result.Rejection);
    }

    /// <summary>Without an identifier there is nothing to remember, so the assertion could be replayed for its whole life.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionCarryingNoIdentifier_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey, identifier: null));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ClientAssertionRejection.ClaimsUnacceptable, result.Rejection);
    }

    /// <summary>A key whose configured lifetime has ended is a key the deployment no longer accepts, whatever it can still verify.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionSignedByARetiredKey_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey, lifetime: (VerifiedAt - TimeSpan.FromDays(1)).ToString("O"));

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ClientAssertionRejection.SignatureUnrecognized, result.Rejection);
    }

    /// <summary>An unsigned document is not a weaker credential, it is no credential, and the permitted algorithms have to say so.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnUnsignedAssertion_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        var header = Encode(Utf8($$"""{"alg":"none","typ":"{{ClientAssertion.DeclaredType}}"}"""));
        var payload = Encode(Utf8(
            $$"""{"aud":"{{ClientAssertion.AdminAudience}}","exp":{{(VerifiedAt + TimeSpan.FromSeconds(60)).ToUnixTimeSeconds()}},"jti":"unsigned"}"""));

        // Act
        var result = await harness.AuthenticateAsync($"{header}.{payload}.");

        // Assert
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AuthenticateAsync_AHeaderCarryingNothing_IsRefusedAsAMissingCredential(string? headerValue)
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateRawAsync(headerValue);

        // Assert
        Assert.Equal(ClientAssertionRejection.CredentialMissing, result.Rejection);
    }

    [Fact]
    public async Task AuthenticateAsync_AHeaderCarryingAnotherScheme_IsRefusedAsMalformed()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateRawAsync("Basic dXNlcjpwYXNz");

        // Assert
        Assert.Equal(ClientAssertionRejection.CredentialMalformed, result.Rejection);
    }

    /// <summary>An opaque credential reaching this scheme is refused before any key material is resolved, so a wrong-shaped credential costs the host nothing.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACredentialThatIsNoAssertion_IsRefusedAsOne()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessFor(clientKey);

        // Act
        var result = await harness.AuthenticateRawAsync("Bearer an-opaque-api-key");

        // Assert
        Assert.Equal(ClientAssertionRejection.NotAnAssertion, result.Rejection);
    }

    private static Harness HarnessFor(AsymmetricAlgorithm clientKey, string? lifetime = null)
    {
        var configuredKey = new ConfiguredSecret
        {
            Name = KeyName,
            SecretReference = $"plaintext:{PublicHalfOf(clientKey)}",
        };

        if (lifetime is not null)
        {
            configuredKey.Lifetime = lifetime;
        }

        var clock = new FakeTimeProvider(VerifiedAt);

        return new Harness(
            new ClientAssertionAuthenticator(
                new PlaintextOnlySecretReferenceResolver(),
                new ClientAssertionReplayStore(clock),
                clock,
                new RecordingLogger<ClientAssertionAuthenticator>()),
            [configuredKey]);
    }

    private static string PublicHalfOf(AsymmetricAlgorithm clientKey) => clientKey switch
    {
        ECDsa ecdsa => ecdsa.ExportSubjectPublicKeyInfoPem(),
        RSA rsa => rsa.ExportSubjectPublicKeyInfoPem(),
        _ => throw new NotSupportedException("The test key is of a kind this harness does not export."),
    };

    /// <summary>Mints one assertion, with every part a test may need to make wrong left open.</summary>
    /// <remarks>The document is written here rather than through the minter, because several tests need a claim the minter is written never to produce.</remarks>
    private static string Presenting(
        AsymmetricAlgorithm clientKey,
        string audience = ClientAssertion.AdminAudience,
        DateTimeOffset? expiresAt = null,
        string? identifier = "an-identifier")
    {
        var algorithmName = ClientAssertionSignature.AlgorithmFor(clientKey);
        var expiry = (expiresAt ?? VerifiedAt + TimeSpan.FromSeconds(60)).ToUnixTimeSeconds();

        var claims = identifier is null
            ? $$"""{"aud":"{{audience}}","exp":{{expiry}}}"""
            : $$"""{"aud":"{{audience}}","exp":{{expiry}},"jti":"{{identifier}}"}""";

        var header = Encode(Utf8($$"""{"alg":"{{algorithmName}}","typ":"{{ClientAssertion.DeclaredType}}"}"""));
        var payload = Encode(Utf8(claims));
        var signingInput = $"{header}.{payload}";

        var signature = ClientAssertionSignature.Sign(clientKey, Encoding.ASCII.GetBytes(signingInput));

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    private static byte[] Utf8(string document) => Encoding.UTF8.GetBytes(document);

    private static string Encode(ReadOnlySpan<byte> document) => Base64Url.EncodeToString(document);

    /// <summary>The authenticator under test, together with the keys the surface configured.</summary>
    private sealed record Harness(ClientAssertionAuthenticator Authenticator, IReadOnlyList<ConfiguredSecret> Keys)
    {
        internal Task<ClientAssertionAuthenticationResult> AuthenticateAsync(
            string assertion,
            string audience = ClientAssertion.AdminAudience) =>
            this.AuthenticateRawAsync($"Bearer {assertion}", audience);

        internal Task<ClientAssertionAuthenticationResult> AuthenticateRawAsync(
            string? headerValue,
            string audience = ClientAssertion.AdminAudience) =>
            this.Authenticator.AuthenticateAsync([.. this.Keys], audience, headerValue, TestContext.Current.CancellationToken);
    }
}
