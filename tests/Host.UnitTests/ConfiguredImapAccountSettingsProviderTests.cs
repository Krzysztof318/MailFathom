// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailMcp.Host.UnitTests;

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
        using (settings.Secrets)
        {
            Assert.Equal("primary", settings.AccountId);
            Assert.Equal("dev-password", settings.Secrets.Password.RevealAsString());
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
        using (first.Secrets)
        using (second.Secrets)
        {
            Assert.NotSame(first.Secrets.Password, second.Secrets.Password);
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
        finished.Secrets.Dispose();

        // Assert
        using (inFlight.Secrets)
        {
            Assert.Equal("dev-password", inFlight.Secrets.Password.RevealAsString());
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

    private static ConfiguredImapAccountSettingsProvider CreateProvider()
    {
        var options = new MailSynchronizationOptions
        {
            Accounts =
            [
                new MailSynchronizationAccountOptions
                {
                    AccountId = "primary",
                    Host = "imap.example.test",
                    UserName = "mailmcp@example.test",
                    Secrets = new MailAccountSecretOptions
                    {
                        Password = new ConfiguredSecret { SecretReference = "plaintext:dev-password" },
                    },
                },
            ],
        };

        return new ConfiguredImapAccountSettingsProvider(
            Options.Create(options),
            new PlaintextOnlySecretReferenceResolver());
    }
}
