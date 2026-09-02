// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Security.ApiKeys;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.ApiKeys;

/// <summary>Covers which presented key admits an owner and which of the several ways of failing are told apart.</summary>
/// <remarks>
/// A key nobody holds, a key whose row is disabled, and a key that resolves no owner are one answer, because telling
/// them apart is what would let a caller learn which keys exist. What is told apart is a request that presented no
/// credential at all, since that is what a challenge is answered to.
/// </remarks>
public sealed class OwnerApiKeyAuthenticatorTests
{
    private static readonly MailOwnerId Owner = MailOwnerId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000002");

    private static readonly IReadOnlyList<MailFathomPermission> Grant = [MailFathomPermission.MailRead];

    [Fact]
    public async Task AuthenticateAsync_AKeyTheDeploymentHolds_AdmitsTheOwnerItResolvesToWithTheGrantOnTheRow()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);

        // Act
        var result = await harness.AuthenticateAsync($"Bearer {harness.MintedKey}");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Admitted);
        Assert.Equal(CredentialId, result.Admitted.CredentialId);
        Assert.Equal(Owner, result.Admitted.Owner);
        Assert.Equal(Grant, result.Admitted.Permissions);
        Assert.Null(result.Rejection);
    }

    /// <summary>The stored digest is what a row is found by, so the key itself never reaches the store.</summary>
    [Fact]
    public async Task AuthenticateAsync_AKeyTheDeploymentHolds_ResolvesTheRowByItsDigestAndNotByTheKey()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);

        // Act
        await harness.AuthenticateAsync($"Bearer {harness.MintedKey}");

        // Assert
        await harness.Credentials.Received(1).FindAsync(
            OwnerCredentialMethod.ApiKey,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value != harness.MintedKey),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthenticateAsync_NoAuthorizationHeaderAtAll_IsRefusedAsAMissingCredential()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(authorizationHeaderValue: null);

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialMissing, result.Rejection);
    }

    /// <summary>Everything a key cannot be is one refusal, and none of it reaches a query.</summary>
    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer not-a-key")]
    public async Task AuthenticateAsync_SomethingThatIsNoKey_IsRefusedWithoutReadingACredential(string headerValue)
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(headerValue);

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialMalformed, result.Rejection);

        await harness.Credentials.DidNotReceiveWithAnyArgs()
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A key whose shape is right and whose row is not there resolves nobody, and says nothing about which.</summary>
    [Fact]
    public async Task AuthenticateAsync_AWellShapedKeyNobodyHolds_IsRefusedAsAnUnrecognizedCredential()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync($"Bearer {harness.MintedKey}");

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialUnrecognized, result.Rejection);
        Assert.Null(result.Admitted);
    }

    /// <summary>Disabling is what stops a key working without deleting it, so the row is read and then refused.</summary>
    [Fact]
    public async Task AuthenticateAsync_ADisabledCredential_IsRefusedTheSameWayAnUnknownKeyIs()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        harness.Holds(enabled: false);

        // Act
        var result = await harness.AuthenticateAsync($"Bearer {harness.MintedKey}");

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialUnrecognized, result.Rejection);
    }

    /// <summary>Builds the authenticator over the real minter, so the digest under test is the one a deployment stores.</summary>
    private sealed class AuthenticatorHarness
    {
        private readonly OwnerApiKeyMinter minter = new();

        internal AuthenticatorHarness()
        {
            var minted = this.minter.Mint();
            this.MintedKey = minted.Key;
            this.MintedLookup = minted.Lookup;

            this.Credentials = Substitute.For<IOwnerCredentialStore>();
            this.Credentials.FindAsync(
                    Arg.Any<OwnerCredentialMethod>(),
                    Arg.Any<OwnerCredentialLookup>(),
                    Arg.Any<CancellationToken>())
                .Returns((ResolvedOwnerCredential?)null);

            this.Authenticator = new OwnerApiKeyAuthenticator(this.Credentials, this.minter);
        }

        internal OwnerApiKeyAuthenticator Authenticator { get; }

        internal IOwnerCredentialStore Credentials { get; }

        internal string MintedKey { get; }

        private OwnerCredentialLookup MintedLookup { get; }

        internal void Holds(bool enabled) =>
            this.Credentials.FindAsync(
                    OwnerCredentialMethod.ApiKey,
                    this.MintedLookup,
                    Arg.Any<CancellationToken>())
                .Returns(new ResolvedOwnerCredential(
                    CredentialId,
                    Owner,
                    OwnerCredentialMethod.ApiKey,
                    Grant,
                    enabled,
                    Material: null));

        internal Task<OwnerApiKeyAuthenticationResult> AuthenticateAsync(string? authorizationHeaderValue) =>
            this.Authenticator.AuthenticateAsync(authorizationHeaderValue, TestContext.Current.CancellationToken);
    }
}
