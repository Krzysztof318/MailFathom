// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class MailAccountSecretOptionsTests
{
    [Fact]
    public async Task FindConfigurationErrorsAsync_ResolvableReferences_ReportsNoError()
    {
        // Arrange
        var options = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "plaintext:dev-password" },
        };

        // Act
        var errors = await options.FindConfigurationErrorsAsync(
            new StubSecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task FindConfigurationErrorsAsync_UnresolvablePasswordReference_ReportsTheFailureAgainstThePasswordBlock()
    {
        // Arrange
        var options = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "file:/run/secrets/absent" },
        };

        // Act
        var errors = await options.FindConfigurationErrorsAsync(
            new StubSecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountSecretOptions.Password), error.PropertyName);
        Assert.Equal(SecretResolutionFailure.MaterialNotFound, error.Failure);
    }

    [Fact]
    public async Task FindConfigurationErrorsAsync_EmptyPasswordSecretReference_ReportsReferenceMissing()
    {
        // Arrange
        var options = new MailAccountSecretOptions { Password = new ConfiguredSecret() };

        // Act
        var errors = await options.FindConfigurationErrorsAsync(
            new StubSecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(SecretResolutionFailure.ReferenceMissing, error.Failure);
    }

    /// <summary>
    /// An absent block is a token-authenticated account rather than a mistake, and reporting it here would fail
    /// startup for every such account. Whether the account was entitled to omit it is decided by its permitted
    /// mechanisms, in the account's own validation.
    /// </summary>
    [Fact]
    public async Task FindConfigurationErrorsAsync_NoPasswordBlockAtAll_ReportsNothing()
    {
        // Arrange
        var options = new MailAccountSecretOptions();

        // Act
        var errors = await options.FindConfigurationErrorsAsync(
            new StubSecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task FindConfigurationErrorsAsync_PlainTextInThePasswordBlockUnderReferenceOnly_ReportsSchemeMissing()
    {
        // Arrange
        var options = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "a-pasted-password" },
        };

        // Act
        var errors = await options.FindConfigurationErrorsAsync(
            new StubSecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(SecretResolutionFailure.SchemeMissing, error.Failure);
    }

    [Fact]
    public async Task FindConfigurationErrorsAsync_EveryError_CarriesNoSecretMaterial()
    {
        // Arrange
        var options = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "plaintext:top-secret-password" },
        };

        // Act
        var errors = await options.FindConfigurationErrorsAsync(
            new StubSecretReferenceResolver { FailEverythingWith = SecretResolutionFailure.ProviderUnavailable },
            CancellationToken.None);

        // Assert
        var error = Assert.Single(errors);
        Assert.DoesNotContain("top-secret-password", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvePasswordAsync_ResolvableReference_ReturnsTheResolvedPassword()
    {
        // Arrange
        var options = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "plaintext:dev-password" },
        };

        // Act
        using var password = await options.ResolvePasswordAsync(new StubSecretReferenceResolver(), CancellationToken.None);

        // Assert
        Assert.Equal("dev-password", password.RevealAsString());
    }

    [Fact]
    public async Task ResolvePasswordAsync_UnresolvableReference_FailsClosedInsteadOfConnecting()
    {
        // Arrange
        var options = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "file:/run/secrets/absent" },
        };

        // Act, Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            options.ResolvePasswordAsync(new StubSecretReferenceResolver(), CancellationToken.None));
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_ResolvedConnectionMaterial_ErasesThePasswordMaterial()
    {
        // Arrange
        var options = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "plaintext:dev-password" },
        };
        var material = new MailAccountConnectionMaterial(
            await options.ResolvePasswordAsync(new StubSecretReferenceResolver(), CancellationToken.None),
            TrustedCertificateAuthority: null);

        // Act
        material.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => material.Password!.RevealAsString());
    }

    /// <summary>Resolves the four shipped schemes without touching the file system or the environment block.</summary>
    private sealed class StubSecretReferenceResolver : ISecretReferenceResolver
    {
        public SecretResolutionFailure? FailEverythingWith { get; init; }

        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
        {
            if (this.FailEverythingWith is { } forcedFailure)
            {
                return Task.FromResult(SecretResolutionResult.Failed(forcedFailure));
            }

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
