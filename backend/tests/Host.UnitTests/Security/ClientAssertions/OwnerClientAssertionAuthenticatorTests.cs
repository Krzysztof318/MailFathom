// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access.Credentials;
using MailFathom.Common.ClientAssertions;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.ClientAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ClientAssertions;

/// <summary>Covers the key-pair method on a surface whose credentials are records beside the owner they admit.</summary>
/// <remarks>
/// The rules an assertion is judged by are the ones the configured path applies and are covered there. What is new here
/// is the resolution in front of them: the fingerprint the client names in its own <c>kid</c> selects one credential
/// row, and everything that fails to select an enabled row is refused as an unrecognized signature — so a key nobody
/// registered, a credential somebody disabled, and a fingerprint naming a row this deployment does not hold are one
/// answer. The successful result is the other half: it names the owner, which is what makes the request act for a
/// person rather than for whichever owner a read happened to find.
/// </remarks>
public sealed class OwnerClientAssertionAuthenticatorTests
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailOwnerId Owner = MailOwnerId.Create(
        Guid.Parse("6f1c0f6c-1f3f-4a9a-9a1e-0e0f1b2c3d4e"));

    private static readonly Guid CredentialId = Guid.Parse("41d7e2b0-2a3b-4c5d-8e9f-0a1b2c3d4e5f");

    [Fact]
    public async Task AuthenticateAsync_AnAssertionSignedByARegisteredKey_AdmitsTheOwnerThatKeyBelongsTo()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey));

        // Assert
        Assert.Null(result.Rejection);
        Assert.Equal(Owner, result.Admitted?.Owner);
        Assert.Equal(CredentialId, result.Admitted?.CredentialId);
    }

    /// <summary>An RSA client is served on exactly the same terms, so the method is not quietly elliptic-curve only.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionSignedByARegisteredRsaKey_AdmitsTheOwnerThatKeyBelongsTo()
    {
        // Arrange
        using var clientKey = RSA.Create(ClientAssertionKeyMaterial.ShortestRsaModulusInBits);
        var harness = HarnessHolding(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey));

        // Assert
        Assert.Null(result.Rejection);
        Assert.Equal(Owner, result.Admitted?.Owner);
    }

    /// <summary>The fingerprint is what selects the row, so a client naming one nothing resolves is refused before any material is imported.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionNamingAFingerprintNoCredentialHolds_IsRefusedAsUnrecognized()
    {
        // Arrange
        using var registeredKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var strangersKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(registeredKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(strangersKey));

        // Assert
        Assert.Null(result.Admitted);
        Assert.Equal(ClientAssertionRejection.SignatureUnrecognized, result.Rejection);
    }

    /// <summary>
    /// A disabled credential still holds its fingerprint, so the row resolves and the refusal has to come from the
    /// enablement rather than from the lookup — and it has to be the same refusal a stranger's key gets, or the
    /// response would tell a caller which of the two they are.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionSignedByADisabledCredentialsKey_IsRefusedIndistinguishably()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey, enabled: false);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey));

        // Assert
        Assert.Null(result.Admitted);
        Assert.Equal(ClientAssertionRejection.SignatureUnrecognized, result.Rejection);
    }

    /// <summary>
    /// A row whose fingerprint resolves while its material does not is the shape a hand-edited record produces, and it
    /// must refuse rather than reach the validator with nothing to verify against.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_ACredentialWhoseStoredMaterialIsNoKey_IsRefusedAsUnrecognized()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey, material: "not a key");

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey));

        // Assert
        Assert.Null(result.Admitted);
        Assert.Equal(ClientAssertionRejection.SignatureUnrecognized, result.Rejection);
    }

    /// <summary>The audience is what separates reading a mailbox from administering the service, so an assertion minted for one surface is refused at the other.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionMintedForTheAdministrativeSurface_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(
            Presenting(clientKey, audience: ClientAssertion.AdminAudience),
            audience: ClientAssertion.McpAudience);

        // Assert
        Assert.Null(result.Admitted);
        Assert.Equal(ClientAssertionRejection.SignatureUnrecognized, result.Rejection);
    }

    /// <summary>Serving the same assertion twice is the replay a short lifetime alone cannot refuse, so the second presentation is the one that has to fail.</summary>
    [Fact]
    public async Task AuthenticateAsync_TheSameAssertionTwice_RefusesTheSecondPresentation()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey);
        var assertion = Presenting(clientKey);

        // Act
        var first = await harness.AuthenticateAsync(assertion);
        var second = await harness.AuthenticateAsync(assertion);

        // Assert
        Assert.Null(first.Rejection);
        Assert.Equal(ClientAssertionRejection.IdentifierAlreadySpent, second.Rejection);
    }

    /// <summary>A credential minted a day ahead is refused, which is what makes the short lifetime a fact rather than a convention the client observes.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionClaimingALongerLifeThanPermitted_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(
            Presenting(clientKey, expiresAt: VerifiedAt + TimeSpan.FromDays(1)));

        // Assert
        Assert.Null(result.Admitted);
        Assert.Equal(ClientAssertionRejection.ClaimsUnacceptable, result.Rejection);
    }

    /// <summary>Without an identifier there is nothing to remember, so the assertion could be replayed for its whole life.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAssertionCarryingNoIdentifier_IsRefused()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey);

        // Act
        var result = await harness.AuthenticateAsync(Presenting(clientKey, identifier: null));

        // Assert
        Assert.Null(result.Admitted);
        Assert.Equal(ClientAssertionRejection.ClaimsUnacceptable, result.Rejection);
    }

    /// <summary>An opaque credential reaching this scheme is refused before any row is read, so a wrong-shaped credential costs the host no query.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACredentialThatIsNoAssertion_IsRefusedWithoutReadingACredential()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey);

        // Act
        var result = await harness.AuthenticateRawAsync("Bearer mfk_an-opaque-api-key");

        // Assert
        Assert.Equal(ClientAssertionRejection.NotAnAssertion, result.Rejection);
        await harness.Credentials.DidNotReceive().FindAsync(
            Arg.Any<OwnerCredentialMethod>(),
            Arg.Any<OwnerCredentialLookup>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AuthenticateAsync_AHeaderCarryingNothing_IsRefusedAsAMissingCredential(string? headerValue)
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = HarnessHolding(clientKey);

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
        var harness = HarnessHolding(clientKey);

        // Act
        var result = await harness.AuthenticateRawAsync("Basic dXNlcjpwYXNz");

        // Assert
        Assert.Equal(ClientAssertionRejection.CredentialMalformed, result.Rejection);
    }

    /// <summary>Builds a deployment holding exactly one public-key credential, resolved by the fingerprint the reader derives.</summary>
    private static Harness HarnessHolding(
        AsymmetricAlgorithm clientKey,
        bool enabled = true,
        string? material = null)
    {
        Assert.True(OwnerCredentialLookup.TryCreate(FingerprintOf(clientKey), out var lookup));

        var registeredMaterial = PublicHalfOf(clientKey);
        var credentials = Substitute.For<IOwnerCredentialStore>();

        credentials.FindAsync(
                OwnerCredentialMethod.PublicKey,
                Arg.Any<OwnerCredentialLookup>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<OwnerCredentialLookup>() == lookup
                ? new ResolvedOwnerCredential(
                    CredentialId,
                    Owner,
                    OwnerCredentialMethod.PublicKey,
                    MailFathomPermission.PublishedFor(ProtectedSurface.Mail),
                    enabled,
                    material ?? registeredMaterial)
                : null);

        var clock = new FakeTimeProvider(VerifiedAt);

        return new Harness(
            new OwnerClientAssertionAuthenticator(
                credentials,
                new ClientAssertionReplayStore(clock),
                clock,
                NullLogger<OwnerClientAssertionAuthenticator>.Instance),
            credentials);
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
        string audience = ClientAssertion.McpAudience,
        DateTimeOffset? expiresAt = null,
        string? identifier = "an-identifier")
    {
        var algorithmName = ClientAssertionSignature.AlgorithmFor(clientKey);
        var expiry = (expiresAt ?? VerifiedAt + TimeSpan.FromSeconds(60)).ToUnixTimeSeconds();

        var claims = identifier is null
            ? $$"""{"aud":"{{audience}}","exp":{{expiry}}}"""
            : $$"""{"aud":"{{audience}}","exp":{{expiry}},"jti":"{{identifier}}"}""";

        var header = Encode(Utf8(
            $$"""{"alg":"{{algorithmName}}","typ":"{{ClientAssertion.DeclaredType}}","kid":"{{FingerprintOf(clientKey)}}"}"""));
        var payload = Encode(Utf8(claims));
        var signingInput = $"{header}.{payload}";

        var signature = ClientAssertionSignature.Sign(clientKey, Encoding.ASCII.GetBytes(signingInput));

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>Derives the name a client puts in its own <c>kid</c>: the base64url SHA-256 of the key's subject public key info.</summary>
    /// <remarks>
    /// Computed here rather than through the reader the provisioning uses, so the two derivations are independent. A
    /// change to either that stopped them agreeing would leave every registered client unable to name its own key, and
    /// a test deriving the value the same way the production code does could not report that.
    /// </remarks>
    private static string FingerprintOf(AsymmetricAlgorithm clientKey)
    {
        var subjectPublicKeyInfo = clientKey switch
        {
            ECDsa ecdsa => ecdsa.ExportSubjectPublicKeyInfo(),
            RSA rsa => rsa.ExportSubjectPublicKeyInfo(),
            _ => throw new NotSupportedException("The test key is of a kind this harness does not export."),
        };

        return Base64Url.EncodeToString(SHA256.HashData(subjectPublicKeyInfo));
    }

    private static byte[] Utf8(string document) => Encoding.UTF8.GetBytes(document);

    private static string Encode(ReadOnlySpan<byte> document) => Base64Url.EncodeToString(document);

    /// <summary>The authenticator under test, beside the store it resolves credentials through.</summary>
    private sealed record Harness(
        OwnerClientAssertionAuthenticator Authenticator,
        IOwnerCredentialStore Credentials)
    {
        internal Task<OwnerClientAssertionAuthenticationResult> AuthenticateAsync(
            string assertion,
            string audience = ClientAssertion.McpAudience) =>
            this.AuthenticateRawAsync($"Bearer {assertion}", audience);

        internal Task<OwnerClientAssertionAuthenticationResult> AuthenticateRawAsync(
            string? headerValue,
            string audience = ClientAssertion.McpAudience) =>
            this.Authenticator.AuthenticateAsync(audience, headerValue, TestContext.Current.CancellationToken);
    }
}
