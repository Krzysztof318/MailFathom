// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.Security.ApiKeys;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.ApiKeys;

/// <summary>Covers which presented credentials authenticate, which do not, and what a refusal is allowed to reveal.</summary>
public sealed class ApiKeyAuthenticatorTests
{
    private const string WorkstationKeyMaterial = "8f2c1d5e-workstation";

    private const string LaptopKeyMaterial = "3a7b9e0f-laptop";

    private static readonly DateTimeOffset RequestedAt = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthenticateAsync_TheConfiguredKey_AuthenticatesAndNamesTheKeyItMatched()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [Key("workstation", WorkstationKeyMaterial)],
            $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("workstation", result.AuthenticatedKeyName?.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_TheSecondOfSeveralKeys_AuthenticatesUnderItsOwnName()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [Key("workstation", WorkstationKeyMaterial), Key("laptop", LaptopKeyMaterial)],
            $"Bearer {LaptopKeyMaterial}");

        // Assert
        Assert.Equal("laptop", result.AuthenticatedKeyName?.Value);
    }

    /// <summary>Rotation is two unexpired keys serving at once, so neither client loses access while it moves.</summary>
    [Fact]
    public async Task AuthenticateAsync_TwoOverlappingUnexpiredKeys_BothAuthenticate()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        var configuredKeys = new[] { Key("retiring", WorkstationKeyMaterial), Key("replacement", LaptopKeyMaterial) };

        // Act
        var retiring = await harness.AuthenticateAsync(configuredKeys, $"Bearer {WorkstationKeyMaterial}");
        var replacement = await harness.AuthenticateAsync(configuredKeys, $"Bearer {LaptopKeyMaterial}");

        // Assert
        Assert.Equal("retiring", retiring.AuthenticatedKeyName?.Value);
        Assert.Equal("replacement", replacement.AuthenticatedKeyName?.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_AKeyNamingNoLifetime_AuthenticatesBecauseTheDefaultIsNoLimit()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        var configuredKey = new ConfiguredSecret
        {
            Name = "workstation",
            SecretReference = $"plaintext:{WorkstationKeyMaterial}",
        };

        // Act
        var result = await harness.AuthenticateAsync([configuredKey], $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(SecretLifetime.NoLimitValue, configuredKey.Lifetime);
    }

    [Fact]
    public async Task AuthenticateAsync_AKeyWhoseLifetimeHasEnded_IsRefused()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        var expiredKey = Key("retired", WorkstationKeyMaterial, lifetime: "2026-07-30T00:00:00Z");

        // Act
        var result = await harness.AuthenticateAsync([expiredKey], $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ApiKeyRejection.CredentialExpired, result.Rejection);
    }

    [Fact]
    public async Task AuthenticateAsync_AKeyExpiringLater_StillAuthenticates()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        var key = Key("workstation", WorkstationKeyMaterial, lifetime: "2027-07-30T00:00:00Z");

        // Act
        var result = await harness.AuthenticateAsync([key], $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.True(result.Succeeded);
    }

    /// <summary>An expired entry left beside its replacement is what a completed rotation looks like, and it must not shut the endpoint.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnExpiredKeyBesideAValidOne_LeavesTheValidOneAuthenticating()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [Key("retired", WorkstationKeyMaterial, lifetime: "2026-07-30T00:00:00Z"), Key("current", LaptopKeyMaterial)],
            $"Bearer {LaptopKeyMaterial}");

        // Assert
        Assert.Equal("current", result.AuthenticatedKeyName?.Value);
    }

    /// <summary>
    /// A lifetime that stopped parsing is a deployment edited into a state startup would have refused. Reading it as
    /// unbounded would open the endpoint on the strength of a typo, so it closes instead.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AKeyWhoseLifetimeNoLongerParses_IsRefusedRatherThanTakenAsUnbounded()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [Key("workstation", WorkstationKeyMaterial, lifetime: "sometime next year")],
            $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_ACredentialMatchingNoConfiguredKey_IsRefused()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [Key("workstation", WorkstationKeyMaterial)],
            "Bearer not-a-configured-key");

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialUnrecognized, result.Rejection);
    }

    /// <summary>The presented credential is compared in full, so a prefix of a real key is as unrecognized as anything else.</summary>
    [Fact]
    public async Task AuthenticateAsync_APrefixOfAConfiguredKey_IsRefused()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [Key("workstation", WorkstationKeyMaterial)],
            $"Bearer {WorkstationKeyMaterial[..8]}");

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialUnrecognized, result.Rejection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthenticateAsync_NoAuthorizationHeader_IsRefusedAsAMissingCredential(string? headerValue)
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync([Key("workstation", WorkstationKeyMaterial)], headerValue);

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialMissing, result.Rejection);
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer8f2c1d5e-workstation")]
    [InlineData("8f2c1d5e-workstation")]
    public async Task AuthenticateAsync_SomethingOtherThanOneBearerCredential_IsRefusedAsMalformed(string headerValue)
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync([Key("workstation", WorkstationKeyMaterial)], headerValue);

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialMalformed, result.Rejection);
    }

    /// <summary>HTTP matches an authentication scheme without regard to case, and a client that spells it its own way still holds a valid key.</summary>
    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    [InlineData("BeArEr")]
    public async Task AuthenticateAsync_TheBearerSchemeInAnyCase_StillAuthenticates(string scheme)
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [Key("workstation", WorkstationKeyMaterial)],
            $"{scheme} {WorkstationKeyMaterial}");

        // Assert
        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// Stopping at the matching key would make a refusal's cost depend on where the presented key sits, and would let an
    /// expired key answer faster than an unrecognized one. Every entry is read on every request, whatever the answer.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AKeyMatchingEarly_StillReadsEveryRemainingKey()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        var configuredKeys = new[]
        {
            Key("first", WorkstationKeyMaterial),
            Key("second", LaptopKeyMaterial),
            Key("third", "0c4d-desktop"),
        };

        // Act
        await harness.AuthenticateAsync(configuredKeys, $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.Equal(3, harness.Resolver.ResolutionCount);
    }

    /// <summary>Nothing is cached, so a key rotated behind an unchanged reference reaches the next request rather than the next restart.</summary>
    [Fact]
    public async Task AuthenticateAsync_EveryRequest_ResolvesTheConfiguredMaterialAgain()
    {
        // Arrange
        var harness = new AuthenticatorHarness();
        var configuredKeys = new[] { Key("workstation", WorkstationKeyMaterial) };

        // Act
        await harness.AuthenticateAsync(configuredKeys, $"Bearer {WorkstationKeyMaterial}");
        await harness.AuthenticateAsync(configuredKeys, $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.Equal(2, harness.Resolver.ResolutionCount);
    }

    [Fact]
    public async Task AuthenticateAsync_AKeyWhoseMaterialDisappeared_IsRefusedAndReportedWithoutTheReference()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [new ConfiguredSecret { Name = "workstation", SecretReference = "file:/run/secrets/absent" }],
            $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.False(result.Succeeded);
        var record = Assert.Single(harness.Logs.Records);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("workstation", Assert.Contains("ApiKeyName", record.Properties));
        Assert.DoesNotContain("/run/secrets", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticateAsync_AKeyWhoseMaterialDisappeared_LeavesTheOtherKeysAuthenticating()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [
                new ConfiguredSecret { Name = "broken", SecretReference = "file:/run/secrets/absent" },
                Key("workstation", WorkstationKeyMaterial),
            ],
            $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.Equal("workstation", result.AuthenticatedKeyName?.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_AKeyCarryingNoUsableName_IsRefusedAndReportedAsAConfigurationFault()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [new ConfiguredSecret { Name = "not a name", SecretReference = $"plaintext:{WorkstationKeyMaterial}" }],
            $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(LogLevel.Error, Assert.Single(harness.Logs.Records).Level);
    }

    [Fact]
    public async Task AuthenticateAsync_AnExpiredKeyPresented_IsRecordedByNameWithoutTheCredential()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        await harness.AuthenticateAsync(
            [Key("retired", WorkstationKeyMaterial, lifetime: "2026-07-30T00:00:00Z")],
            $"Bearer {WorkstationKeyMaterial}");

        // Assert
        var record = Assert.Single(harness.Logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("retired", Assert.Contains("ApiKeyName", record.Properties));
        Assert.DoesNotContain(WorkstationKeyMaterial, record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticateAsync_AnyRefusal_LogsNeitherThePresentedCredentialNorTheConfiguredMaterial()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        await harness.AuthenticateAsync([Key("workstation", WorkstationKeyMaterial)], "Bearer a-guessed-key");

        // Assert
        var reported = string.Join(' ', harness.Logs.Records.Select(record => record.Message));
        Assert.DoesNotContain(WorkstationKeyMaterial, reported, StringComparison.Ordinal);
        Assert.DoesNotContain("a-guessed-key", reported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticateAsync_NoConfiguredKey_RefusesEveryCredential()
    {
        // Arrange
        var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync([], $"Bearer {WorkstationKeyMaterial}");

        // Assert
        Assert.Equal(ApiKeyRejection.CredentialUnrecognized, result.Rejection);
    }

    private static ConfiguredSecret Key(string name, string material, string? lifetime = null) => new()
    {
        Name = name,
        SecretReference = $"plaintext:{material}",
        Lifetime = lifetime ?? SecretLifetime.NoLimitValue,
    };

    /// <summary>
    /// A key file written by <c>echo</c>, provisioned as a Compose secret, or mounted by Kubernetes routinely ends in a
    /// newline, which is why <see cref="ResolvedSecret" /> removes one from its text view. Digesting the raw bytes
    /// instead would let the deployment start cleanly and then refuse every client presenting the key an operator can
    /// actually see — a failure with no symptom on the server but a total one at every client.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_KeyMaterialEndingInANewline_StillAuthenticatesTheVisibleCredential()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var authenticator = new ApiKeyAuthenticator(
            new NewlineTerminatedResolver(WorkstationKeyMaterial),
            new FakeTimeProvider(RequestedAt),
            loggerFactory.CreateLogger<ApiKeyAuthenticator>());

        // Act
        var result = await authenticator.AuthenticateAsync(
            [Key("workstation", WorkstationKeyMaterial)],
            $"Bearer {WorkstationKeyMaterial}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("workstation", result.AuthenticatedKeyName?.Value);
    }

    /// <summary>Resolves every reference to one material with a trailing newline, the way a mounted key file arrives.</summary>
    private sealed class NewlineTerminatedResolver(string material) : ISecretReferenceResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken) =>
            Task.FromResult(SecretResolutionResult.Resolved(
                ResolvedSecret.FromText(material + "\n"),
                SecretMaterialSource.SchemeAdapter));
    }

    private sealed class AuthenticatorHarness
    {
        private readonly ApiKeyAuthenticator authenticator;

        internal AuthenticatorHarness()
        {
            using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(this.Logs));

            this.authenticator = new ApiKeyAuthenticator(
                this.Resolver,
                new FakeTimeProvider(RequestedAt),
                loggerFactory.CreateLogger<ApiKeyAuthenticator>());
        }

        internal RecordingLoggerProvider Logs { get; } = new();

        internal CountingPlaintextResolver Resolver { get; } = new();

        internal Task<ApiKeyAuthenticationResult> AuthenticateAsync(
            IReadOnlyList<ConfiguredSecret> configuredKeys,
            string? authorizationHeaderValue) => this.authenticator.AuthenticateAsync(
                configuredKeys,
                authorizationHeaderValue,
                TestContext.Current.CancellationToken);
    }

    /// <summary>Resolves the <c>plaintext:</c> scheme only, and counts how often it was asked.</summary>
    private sealed class CountingPlaintextResolver : ISecretReferenceResolver
    {
        private int resolutionCount;

        internal int ResolutionCount => this.resolutionCount;

        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.resolutionCount);

            if (!SecretReference.TryParse(configuredValue, out var reference, out var grammarFailure))
            {
                return Task.FromResult(SecretResolutionResult.Failed(grammarFailure));
            }

            return Task.FromResult(reference.Scheme == SecretReferenceScheme.Plaintext
                ? SecretResolutionResult.Resolved(
                    ResolvedSecret.FromText(reference.Target),
                    SecretMaterialSource.SchemeAdapter)
                : SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound));
        }
    }
}
