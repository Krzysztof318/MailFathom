// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets;
using Xunit;

namespace MailFathom.Host.UnitTests;

public sealed class ConfiguredImapAccountSettingsProviderTests
{
    [Fact]
    public async Task GetSettingsAsync_ConfiguredAccount_ReturnsSettingsCarryingTheResolvedPassword()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var settings = await provider.GetSettingsAsync("primary", CancellationToken.None);

        // Assert
        using (settings.Material)
        {
            Assert.Equal("primary", settings.AccountId);
            Assert.Equal("dev-password", settings.Material.Password.RevealAsString());
        }
    }

    /// <summary>Nothing is cached, so material rotated behind an unchanged reference reaches the next connection attempt.</summary>
    [Fact]
    public async Task GetSettingsAsync_CalledTwice_ResolvesFreshMaterialEachTime()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var first = await provider.GetSettingsAsync("primary", CancellationToken.None);
        var second = await provider.GetSettingsAsync("primary", CancellationToken.None);

        // Assert
        using (first.Material)
        using (second.Material)
        {
            Assert.NotSame(first.Material.Password, second.Material.Password);
        }
    }

    /// <summary>Disposing one caller's settings must not erase material another caller is still using.</summary>
    [Fact]
    public async Task GetSettingsAsync_OneCallersSecretsDisposed_LeavesAnotherCallersMaterialIntact()
    {
        // Arrange
        var provider = CreateProvider();
        var inFlight = await provider.GetSettingsAsync("primary", CancellationToken.None);

        // Act
        var finished = await provider.GetSettingsAsync("primary", CancellationToken.None);
        finished.Material.Dispose();

        // Assert
        using (inFlight.Material)
        {
            Assert.Equal("dev-password", inFlight.Material.Password.RevealAsString());
        }
    }

    /// <summary>Rotating the credential file behind an unchanged reference must reach the next connection attempt.</summary>
    [Fact]
    public async Task GetSettingsAsync_MaterialRotatedBehindAnUnchangedReference_ResolvesTheRotatedPasswordForTheNextAttempt()
    {
        // Arrange
        var resolver = new RotatingSecretReferenceResolver("first-password");
        var provider = CreateProvider(resolver);
        var inFlight = await provider.GetSettingsAsync("primary", CancellationToken.None);

        // Act
        resolver.Rotate("second-password");
        var afterRotation = await provider.GetSettingsAsync("primary", CancellationToken.None);

        // Assert
        using (inFlight.Material)
        using (afterRotation.Material)
        {
            Assert.Equal("second-password", afterRotation.Material.Password.RevealAsString());
            Assert.Equal("first-password", inFlight.Material.Password.RevealAsString());
        }
    }

    [Fact]
    public async Task GetSettingsAsync_UnknownAccount_Throws()
    {
        // Arrange
        var provider = CreateProvider();

        // Act, Assert
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            provider.GetSettingsAsync("absent", CancellationToken.None));
    }

    private static ConfiguredImapAccountSettingsProvider CreateProvider(ISecretReferenceResolver? resolver = null)
    {
        var options = new MailSynchronizationOptions
        {
            Accounts =
            [
                new MailSynchronizationAccountOptions
                {
                    AccountId = "primary",
                    Host = "imap.example.test",
                    UserName = "mailfathom@example.test",
                    Secrets = new MailAccountSecretOptions
                    {
                        Password = new ConfiguredSecret { SecretReference = "plaintext:dev-password" },
                    },
                },
            ],
        };

        var secretReferenceResolver = resolver ?? new PlaintextOnlySecretReferenceResolver();

        return new ConfiguredImapAccountSettingsProvider(
            options,
            secretReferenceResolver,
            new TrustAnchorLoader(secretReferenceResolver));
    }

    /// <summary>Stands in for a credential file whose contents change while the reference naming it does not.</summary>
    private sealed class RotatingSecretReferenceResolver(string initialMaterial) : ISecretReferenceResolver
    {
        private string material = initialMaterial;

        public void Rotate(string rotatedMaterial) => this.material = rotatedMaterial;

        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken) =>
            Task.FromResult(SecretResolutionResult.Resolved(
                ResolvedSecret.FromText(this.material),
                SecretMaterialSource.SchemeAdapter));
    }
}
